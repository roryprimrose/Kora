using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task GlobalNativeTypedAndActivatedSettingsNeverEnableListeningOrConsent()
    {
        await using var ptt = new GlobalPttConfigurationServiceTests.Fixture();
        var f = new Fixture();
        var applied = new List<GlobalPttShortcut?>();
        f.ViewModel.BindGlobalPtt(ptt.Service, shortcut =>
        {
            applied.Add(shortcut);
            f.ViewModel.ReportGlobalPttRegistration("Registration test readback.");
            return Task.CompletedTask;
        });
        f.ViewModel.Invoking(main => main.BindGlobalPtt(ptt.Service, _ => Task.CompletedTask))
            .Should().Throw<InvalidOperationException>();
        f.VoiceConsent.Consent = false;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.IsGlobalPttHostEligible.Should().BeFalse();
        f.ViewModel.CanChangeGlobalPtt.Should().BeFalse();
        f.ViewModel.BindGlobalPttNativeLifetime(static () => true);
        f.ViewModel.CanChangeGlobalPtt.Should().BeTrue();
        f.ViewModel.GlobalPttChoices.Should().Equal(GlobalPttShortcut.Choices);
        f.ViewModel.SelectedGlobalPttShortcut = GlobalPttShortcut.ControlShiftSpace;
        await f.ViewModel.CancelGlobalPttCommand.ExecuteAsync();
        f.ViewModel.SelectedGlobalPttShortcut.Should().Be(GlobalPttShortcut.Unassigned);
        f.ViewModel.SelectedGlobalPttShortcut = GlobalPttShortcut.ControlShiftF9;
        await f.ViewModel.SaveGlobalPttCommand.ExecuteAsync();
        applied.Should().Equal(null, GlobalPttShortcut.ControlShiftF9);
        f.ViewModel.EffectiveGlobalPttShortcut.Should().Be(GlobalPttShortcut.ControlShiftF9);
        await f.RunAsync("get global push-to-talk shortcut");
        f.ViewModel.Transcript.Should().Contain("Control Shift F9").And.Contain("Registration test readback");
        await f.RunAsync("set global push-to-talk to Control C");
        f.ViewModel.Transcript.Should().Contain("reserved");
        await f.RunAsync("set global push-to-talk to Control Shift Space");
        f.ViewModel.EffectiveGlobalPttShortcut.Should().Be(GlobalPttShortcut.ControlShiftSpace);
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await f.ViewModel.SetVoiceConsentAsync(true);
        f.ViewModel.IsGlobalPttHostEligible.Should().BeTrue();
        var before = f.Voice.StartCalls;
        await f.RaiseActivatedTranscriptAsync("Kora, reset global push-to-talk shortcut", 1);
        f.ViewModel.EffectiveGlobalPttShortcut.Should().Be(GlobalPttShortcut.Unassigned);
        ptt.Inner.Audit.Events.Should().Contain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        f.Voice.StartCalls.Should().Be(before + 1);
        f.ViewModel.Dispose();
        f.ViewModel.IsGlobalPttHostEligible.Should().BeFalse();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("native")]
    [InlineData("owner")]
    [InlineData("pending")]
    [InlineData("disposed")]
    public async Task GlobalShortcutUnavailableHostWritesNothing(string boundary)
    {
        await using var ptt = new GlobalPttConfigurationServiceTests.Fixture();
        var f = new Fixture();
        if (boundary is not "missing") { f.ViewModel.BindGlobalPtt(ptt.Service, _ => Task.CompletedTask); }
        await using var pending = new PendingDetailsQuestion(f);
        if (boundary is "pending")
        { f.Probe.Status = new("local.inference", "Local inference", Kora.Core.Dependencies.DependencyReadiness.Ready, "fixture"); }
        await f.ViewModel.InitializeAsync();
        if (boundary is not "native") { f.ViewModel.BindGlobalPttNativeLifetime(static () => true); }
        if (boundary is "missing")
        {
            f.ViewModel.EffectiveGlobalPttShortcut.Should().BeNull();
            f.ViewModel.GlobalPttStatus.Should().Contain("unavailable");
        }
        if (boundary is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (boundary is "disposed") { f.ViewModel.Dispose(); }
        if (boundary is "pending") { await pending.PresentAsync(); }
        f.ViewModel.CanChangeGlobalPtt.Should().BeFalse();
        await f.ViewModel.ExecuteForegroundPttCommandAsync(
            new(AppearanceCommandOperation.Set, GlobalShortcut: GlobalPttShortcut.ControlShiftF9, IsGlobal: true),
            SecurityAuditInitiator.LocalUser);
        ptt.Inner.Store.Writes.Should().Be(0);
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    [InlineData("call")]
    [InlineData("binding")]
    [InlineData("late-binding")]
    [InlineData("registration")]
    [InlineData("late-registration")]
    public async Task GlobalShortcutFailureAndOriginalAdmissionAreExplicit(string boundary)
    {
        await using var ptt = new GlobalPttConfigurationServiceTests.Fixture();
        if (boundary is "load") { ptt.Inner.Store.LoadFailure = new InvalidDataException(); }
        var f = new Fixture();
        f.ViewModel.BindGlobalPtt(ptt.Service, shortcut =>
        {
            if (shortcut is not null && boundary is "registration") { throw new InvalidOperationException("Conflict; no fallback."); }
            if (shortcut is not null && boundary is "late-registration")
            { f.ViewModel.BindGlobalPttNativeLifetime(static () => false); }
            return Task.CompletedTask;
        });
        f.ViewModel.BindGlobalPttNativeLifetime(static () => true);
        await f.ViewModel.InitializeAsync();
        if (boundary is "save") { ptt.Inner.Store.SaveFailure = new IOException(); }
        if (boundary is "call") { f.CallState.SetState(CallState.Active); }
        if (boundary is "binding")
        {
            ptt.Inner.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == SecurityAuditOutcome.Requested)
                { f.ViewModel.BindGlobalPttNativeLifetime(static () => true); }
            };
        }
        if (boundary is "late-binding")
        { ptt.Inner.Store.AfterConfirm = () => f.ViewModel.BindGlobalPttNativeLifetime(static () => false); }
        ptt.Inner.Audit.BeforeWrite += item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested)
            { f.ViewModel.CanChangeGlobalPtt.Should().BeFalse("one configuration owner is committing"); }
        };
        using var origin = boundary is "call" ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request) : null;
        await f.ViewModel.ExecuteForegroundPttCommandAsync(
            new(AppearanceCommandOperation.Set, GlobalShortcut: GlobalPttShortcut.ControlShiftSpace, IsGlobal: true),
            boundary is "call" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser);
        f.ViewModel.Transcript.Should().ContainAny("not available", "denied");
        if (boundary is "load") { f.ViewModel.GlobalPttStatus.Should().Contain("unavailable"); }
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }
}
