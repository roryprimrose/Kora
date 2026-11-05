using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ShortcutInvalidSavedNameRequiresUnprefixedExplicitRecovery()
    {
        await using var ptt = new ForegroundPttConfigurationServiceTests.Fixture();
        var f = new Fixture(foregroundPtt: ptt.Service);
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("Kora, get push-to-talk shortcut");
        f.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        await f.RunAsync("get push-to-talk shortcut");
        f.ViewModel.Transcript.Should().Contain("Unassigned");
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ShortcutNativeTypedAndActivatedSettingsShareValidationWithoutStartingCapture()
    {
        await using var ptt = new ForegroundPttConfigurationServiceTests.Fixture();
        var f = new Fixture(foregroundPtt: ptt.Service);
        f.VoiceConsent.Consent = false;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.CanChangeForegroundPtt.Should().BeFalse();
        f.ViewModel.BindForegroundPttNativeLifetime(static () => true);
        f.ViewModel.ForegroundPttChoices.Should().Equal(ForegroundPttShortcut.Choices);
        f.ViewModel.SelectedForegroundPttShortcut = ForegroundPttShortcut.ControlShiftSpace;
        await f.ViewModel.CancelForegroundPttCommand.ExecuteAsync();
        f.ViewModel.SelectedForegroundPttShortcut.Should().Be(ForegroundPttShortcut.Unassigned);
        f.ViewModel.SelectedForegroundPttShortcut = ForegroundPttShortcut.ControlShiftSpace;
        await f.ViewModel.SaveForegroundPttCommand.ExecuteAsync();
        f.ViewModel.EffectiveForegroundPttShortcut.Should().Be(ForegroundPttShortcut.ControlShiftSpace);
        await f.RunAsync("get push-to-talk shortcut");
        f.ViewModel.Transcript.Should().Contain("Control Shift Space");
        await f.RunAsync("set push-to-talk to Control C");
        f.ViewModel.Transcript.Should().Contain("reserved");
        f.ViewModel.EffectiveForegroundPttShortcut.Should().Be(ForegroundPttShortcut.ControlShiftSpace);
        await f.RunAsync("set push-to-talk to Control Shift F9");
        f.ViewModel.EffectiveForegroundPttShortcut.Should().Be(ForegroundPttShortcut.ControlShiftF9);
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await f.ViewModel.SetVoiceConsentAsync(true);
        var before = f.Voice.StartCalls;
        await f.RaiseActivatedTranscriptAsync("Kora, reset push-to-talk shortcut", 1);
        f.ViewModel.EffectiveForegroundPttShortcut.Should().Be(ForegroundPttShortcut.Unassigned);
        ptt.Audit.Events.Should().Contain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        f.Voice.StartCalls.Should().Be(before + 1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("native")]
    [InlineData("owner")]
    [InlineData("pending")]
    [InlineData("disposed")]
    public async Task ShortcutUnavailableHostsAndPendingInteractionsWriteNothing(string boundary)
    {
        await using var ptt = new ForegroundPttConfigurationServiceTests.Fixture();
        var f = new Fixture(foregroundPtt: boundary is "missing" ? null : ptt.Service);
        if (boundary is "pending")
        {
            f.Probe.Status = new("local.inference", "Local inference", Kora.Core.Dependencies.DependencyReadiness.Ready, "fixture");
            f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        }
        await f.ViewModel.InitializeAsync();
        if (boundary is not "native") { f.ViewModel.BindForegroundPttNativeLifetime(static () => true); }
        if (boundary is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (boundary is "disposed") { f.ViewModel.Dispose(); }
        if (boundary is "pending")
        {
            await f.RunAsync("help me");
            await f.ViewModel.ActiveReasoningTask!;
            f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        }
        f.ViewModel.CanChangeForegroundPtt.Should().BeFalse();
        await f.ViewModel.SaveForegroundPttCommand.ExecuteAsync();
        ptt.Store.Writes.Should().Be(0);
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    [InlineData("call")]
    [InlineData("binding")]
    [InlineData("late-binding")]
    public async Task ShortcutFailuresAndOriginalChannelDenialAreExplicit(string boundary)
    {
        await using var ptt = new ForegroundPttConfigurationServiceTests.Fixture();
        if (boundary is "load") { ptt.Store.LoadFailure = new InvalidDataException(); }
        var f = new Fixture(foregroundPtt: ptt.Service);
        f.ViewModel.BindForegroundPttNativeLifetime(static () => true);
        await f.ViewModel.InitializeAsync();
        if (boundary is "save") { ptt.Store.SaveFailure = new IOException(); }
        if (boundary is "call") { f.CallState.SetState(CallState.Active); }
        if (boundary is "binding")
        {
            ptt.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == SecurityAuditOutcome.Requested)
                { f.ViewModel.BindForegroundPttNativeLifetime(static () => true); }
            };
        }
        if (boundary is "late-binding")
        { ptt.Store.AfterConfirm = () => f.ViewModel.BindForegroundPttNativeLifetime(static () => false); }
        ptt.Audit.BeforeWrite += item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested)
            { f.ViewModel.CanChangeForegroundPtt.Should().BeFalse("the committing setting has one owner"); }
        };
        using var origin = boundary is "call" ? Kora.Core.Diagnostics.HostActivity.BeginRoot(
            HostRequest.Create(RequestOrigin.ActivatedVoice), Kora.Core.Diagnostics.HostActivityLayer.Application,
            Kora.Core.Diagnostics.HostOperation.Request) : null;
        await f.ViewModel.ExecuteForegroundPttCommandAsync(
            new(AppearanceCommandOperation.Set, ForegroundPttShortcut.ControlShiftSpace),
            boundary is "call" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser);
        f.ViewModel.Transcript.Should().ContainAny("not confirmed", "denied");
        if (boundary is "load")
        {
            f.ViewModel.ForegroundPttStatus.Should().Contain("unavailable");
            await f.ViewModel.CancelForegroundPttCommand.ExecuteAsync();
        }
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }
}
