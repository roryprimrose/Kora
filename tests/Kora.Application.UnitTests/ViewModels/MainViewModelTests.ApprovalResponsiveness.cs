using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ConsentWithdrawalClosesCaptureBeforeSlowPersistenceAndTheLatestChoiceWins()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.IsListening.Should().BeTrue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.VoiceConsent.BeforeSave = consent =>
        {
            if (!consent) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var granting = fixture.ViewModel.SetVoiceConsentAsync(true);
        Task withdrawal;
        try
        {
            await entered.Task.WaitAsync(token);
            withdrawal = fixture.ViewModel.SetVoiceConsentAsync(false);
            fixture.ViewModel.HasVoiceConsent.Should().BeFalse();
            fixture.Voice.IsListening.Should().BeFalse();
            fixture.Voice.StopCalls.Should().BeGreaterThan(0);
            withdrawal.IsCompleted.Should().BeFalse();
            fixture.ViewModel.ShowSettings();
        }
        finally { release.Set(); }
        await Task.WhenAll(granting, withdrawal);
        fixture.VoiceConsent.Consent.Should().BeFalse();
        fixture.ViewModel.HasVoiceConsent.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task CancellingDuringRequestedPowerAuditCannotPublishTheProposalAfterTheReceipt()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "power.shutdown", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var command = fixture.Catalog.GetCommands("Kora").Single(item => item.Action == BuiltInAction.ProposeShutdown);
        var preparing = fixture.ViewModel.ExecuteAsync(command, SecurityAuditInitiator.TypedCommand);
        try
        {
            await entered.Task.WaitAsync(token);
            preparing.IsCompleted.Should().BeFalse();
            await fixture.ViewModel.CancelCurrentTaskAsync();
        }
        finally { release.Set(); }
        await preparing;
        await fixture.RunAsync("what power action is pending");
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
    }
}
