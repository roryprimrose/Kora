using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationExecutionAndLogonStartupKeepIndependentReviewedWrites(bool typed)
    {
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(enableConversationExecution: true, logonStartupConfiguration: startup.Service);
        f.ViewModel.BindLogonStartupNativeLifetime(static () => true);
        f.ViewModel.CanChangeConversationExecution.Should().BeTrue();
        f.ViewModel.CanControlLogonStartupNative.Should().BeTrue();
        if (typed) { await f.RunAsync("review conversations.execution-maximum to 1"); }
        else
        {
            f.ViewModel.SelectedConversationExecutionLimit = f.ViewModel.ConversationExecutionLimits[0];
            await f.ViewModel.ReviewConversationExecutionCommand.ExecuteAsync();
        }
        f.ConversationExecutionPreferences.Writes.Should().Be(0);
        startup.Preferences.Writes.Should().Be(0);
        startup.Registration.Writes.Should().Be(0);
        if (typed) { await f.RunAsync("confirm conversations.execution-maximum"); }
        else { await f.ViewModel.ConfirmConversationExecutionCommand.ExecuteAsync(); }
        f.ConversationExecutionPreferences.Value!.Value.Should().Be(1);
        f.ConversationExecutionPreferences.Writes.Should().Be(1);
        startup.Preferences.Writes.Should().Be(0);
        startup.Registration.Writes.Should().Be(0);
        if (typed) { await f.RunAsync("enable start at logon"); }
        else { await f.ViewModel.EnableLogonStartupCommand.ExecuteAsync(); }
        startup.Preferences.Writes.Should().Be(0);
        startup.Registration.Writes.Should().Be(0);
        if (typed) { await f.RunAsync("confirm start at logon change"); }
        else { await f.ViewModel.ConfirmLogonStartupCommand.ExecuteAsync(); }
        startup.Preferences.Value.Should().BeTrue();
        startup.Registration.State.Enabled.Should().BeTrue();
        f.ConversationExecutionPreferences.Value!.Value.Should().Be(1);
        f.ConversationExecutionPreferences.Writes.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationExecutionNativeAndTypedReviewConfirmConsumeSamePreference(bool typed)
    {
        var f = new Fixture(enableConversationExecution: true);
        f.ViewModel.ConversationExecutionLimits.Select(limit => limit.Value).Should().Equal(1, 2);
        f.ViewModel.CanChangeConversationExecution.Should().BeTrue();
        f.ViewModel.CanConfirmConversationExecution.Should().BeFalse();
        if (typed)
        {
            await f.RunAsync("get conversations.execution-maximum");
            await f.RunAsync("review conversations.execution-maximum to 1");
        }
        else
        {
            await f.ViewModel.RefreshConversationExecutionCommand.ExecuteAsync();
            f.ViewModel.SelectedConversationExecutionLimit = f.ViewModel.ConversationExecutionLimits[0];
            await f.ViewModel.ReviewConversationExecutionCommand.ExecuteAsync();
        }
        f.ViewModel.ConversationExecutionStatus.Should().Contain("2 -> 1").And.Contain("not qualified effective capacity");
        f.ConversationExecutionPreferences.Writes.Should().Be(0);
        f.ViewModel.CanConfirmConversationExecution.Should().BeTrue();
        if (typed) { await f.RunAsync("confirm conversations.execution-maximum"); }
        else { await f.ViewModel.ConfirmConversationExecutionCommand.ExecuteAsync(); }
        f.ConversationExecutionPreferences.Value!.Value.Should().Be(1);
        f.Audit.Events.Should().Contain(entry => string.Equals(entry.ActionId, "configuration.conversation-execution-maximum", StringComparison.Ordinal)
            && entry.Outcome == SecurityAuditOutcome.Succeeded
            && entry.Initiator == (typed ? SecurityAuditInitiator.TypedCommand : SecurityAuditInitiator.LocalUser));
        if (typed)
        {
            await f.RunAsync("review conversations.execution-maximum to 2");
            await f.RunAsync("cancel conversations.execution-maximum");
        }
        else { await f.ViewModel.CancelConversationExecutionCommand.ExecuteAsync(); }
        f.ViewModel.CanConfirmConversationExecution.Should().BeFalse();
        f.Voice.IsListening.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
        f.ConversationExecutionConfiguration!.Observe();
    }

    [Fact]
    public async Task ConversationExecutionActivatedVoiceKeepsOriginalOriginAndRequiresSeparateConfirmation()
    {
        var f = new Fixture(enableConversationExecution: true);
        await f.RaiseActivatedTranscriptAsync("Kora, review conversations.execution-maximum to 1", 0.9f);
        f.ConversationExecutionPreferences.Writes.Should().Be(0);
        f.ViewModel.CanConfirmConversationExecution.Should().BeTrue();
        await f.RaiseActivatedTranscriptAsync("Kora, confirm conversations.execution-maximum", 0.9f);
        f.ConversationExecutionPreferences.Value!.Value.Should().Be(1);
        f.Audit.Events.Should().Contain(entry => string.Equals(entry.ActionId, "configuration.conversation-execution-maximum", StringComparison.Ordinal)
            && entry.Initiator == SecurityAuditInitiator.VoiceCommand);
        await f.ViewModel.ExecuteConversationExecutionCommandAsync(new(ConversationExecutionOperation.Get), SecurityAuditInitiator.VoiceCommand);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("owner")]
    [InlineData("clarify")]
    [InlineData("no-review")]
    [InlineData("write")]
    [InlineData("denied")]
    [InlineData("late-host")]
    [InlineData("busy")]
    public async Task ConversationExecutionUnavailableStaleOrFailedControlsDoNotDispatch(string boundary)
    {
        var f = new Fixture(enableConversationExecution: boundary is not "missing");
        if (boundary is "missing")
        {
            f.ViewModel.ConversationExecutionStatus.Should().Contain("unavailable");
            f.ViewModel.CanConfirmConversationExecution.Should().BeFalse();
        }
        if (boundary is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (boundary is "clarify") { await f.RunAsync("set conversations.execution-maximum to 3"); }
        else if (boundary is "missing" or "owner" or "no-review")
        { await f.ViewModel.ConfirmConversationExecutionCommand.ExecuteAsync(); }
        else
        {
            await f.ViewModel.ReviewConversationExecutionCommand.ExecuteAsync();
            if (boundary is "write") { f.ConversationExecutionPreferences.Failure = new IOException(); }
            if (boundary is "late-host") { f.ManualCallStore.AfterOperation = () => f.ViewModel.BindCallOwnershipGate(static () => false); }
            if (boundary is "denied")
            {
                f.Audit.BeforeWrite = entry =>
                {
                    if (entry.Outcome == SecurityAuditOutcome.Requested
                        && string.Equals(entry.ActionId, "configuration.conversation-execution-maximum", StringComparison.Ordinal))
                    { f.ViewModel.BindCallOwnershipGate(static () => false); }
                };
            }
            Task? duplicate = null;
            if (boundary is "busy")
            {
                f.ManualCallStore.BeforeOperation = () =>
                {
                    f.ManualCallStore.BeforeOperation = null;
                    duplicate = f.ViewModel.ConfirmConversationExecutionCommand.ExecuteAsync();
                };
            }
            await f.ViewModel.ConfirmConversationExecutionCommand.ExecuteAsync();
            if (duplicate is not null) { await duplicate; }
        }
        f.Voice.IsListening.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ConversationExecutionUnknownActivationPrefixCannotRegisterAReview()
    {
        var f = new Fixture(enableConversationExecution: true);
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("Kora, review conversations.execution-maximum to 1");
        f.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        f.ViewModel.CanConfirmConversationExecution.Should().BeFalse();
        await f.RunAsync("get conversations.execution-maximum");
        f.ViewModel.ResponseBody.Should().Contain("Configured future ordinary-conversation maximum");
        f.ConversationExecutionPreferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }
}
