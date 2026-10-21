using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitRequiresExplicitConversationShutdownAdmission(bool admitted)
    {
        var f = await Fixture.CreateInitializedAsync();
        var closed = false;
        var calls = 0;
        f.ViewModel.WindowActionRequested += (_, action) => closed |= action == WindowAction.Close;
        f.ViewModel.BindConversationShutdown(() => { calls++; return Task.FromResult(admitted); });
        await f.ViewModel.ExitAsync();
        calls.Should().Be(1);
        closed.Should().Be(admitted);
        f.ViewModel.BindConversationShutdown(null);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExitAndRestartRetainInstructionEditorGuardAfterConversationDecision(bool restart, bool editorAdmitted)
    {
        var f = await Fixture.CreateInitializedAsync();
        var sequence = new List<string>();
        f.ViewModel.BindConversationShutdown(() => { sequence.Add("conversation"); return Task.FromResult(true); }, () => true);
        f.ViewModel.BindInstructionEditorShutdown(() => { sequence.Add("editor"); return Task.FromResult(editorAdmitted); });
        if (restart) { await f.RunAsync("restart the app"); }
        else { await f.ViewModel.ExitAsync(); }
        sequence.Should().Equal("conversation", "editor");
        f.WindowActions.Contains(WindowAction.Close).Should().Be(editorAdmitted);
        f.Events.Contains("process.restart").Should().Be(restart && editorAdmitted);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("throw")]
    [InlineData("invalid")]
    [InlineData("validation-throw")]
    [InlineData("initial-owner")]
    [InlineData("source-error")]
    public async Task FailedUnsentReviewCannotDispatchRestartOrEscapeItsCommandBoundary(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindConversationShutdown(
            () => fault is "throw" ? throw new IOException("Synthetic prompt failure.") : Task.FromResult(fault is not "cancel"),
            () => fault is "validation-throw" ? throw new IOException("Synthetic validation failure.") : fault is not "invalid");
        if (fault is "initial-owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (fault is "source-error")
        {
            var fail = false;
            f.ViewModel.BindCallOwnershipGate(() => true, () => fail ? throw new IOException("Synthetic owner source unavailable.") : 1);
            fail = true;
        }
        if (fault is "initial-owner" or "source-error") { await f.ViewModel.ExitAsync(); }
        else { await f.RunAsync("restart the app"); }
        f.Events.Should().NotContain("process.restart");
        f.WindowActions.Should().NotContain(WindowAction.Close);
        f.ViewModel.ResponseBody.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FatalReviewFailureIsNotConvertedToShutdownApproval(bool validation)
    {
        var f = await Fixture.CreateInitializedAsync();
#pragma warning disable CA2201, MA0012 // Inject the fatal boundary deterministically without inducing real memory pressure.
        f.ViewModel.BindConversationShutdown(
            () => validation ? Task.FromResult(true) : throw new OutOfMemoryException("Synthetic fatal review failure."),
            () => throw new OutOfMemoryException("Synthetic fatal validation failure."));
#pragma warning restore CA2201, MA0012
        var exit = () => f.ViewModel.ExitAsync();
        await exit.Should().ThrowAsync<OutOfMemoryException>();
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner-rebound")]
    [InlineData("owner-revision")]
    [InlineData("lock")]
    [InlineData("private")]
    [InlineData("privacy-state")]
    [InlineData("topology")]
    [InlineData("call")]
    [InlineData("binding")]
    [InlineData("validator-binding")]
    [InlineData("dispose")]
    public async Task VoluntaryExitRejectsChangedPrivateSourceAfterAwaitedPrompt(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = 1L;
        f.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        Func<Task<bool>> prepare = () => decision.Task;
        f.ViewModel.BindConversationShutdown(prepare, () => true);
        var exiting = f.ViewModel.ExitAsync();
        exiting.IsCompleted.Should().BeFalse();
        switch (fault)
        {
            case "owner": f.ViewModel.BindCallOwnershipGate(() => false); break;
            case "owner-rebound": f.ViewModel.BindCallOwnershipGate(() => true, () => owner); break;
            case "owner-revision": owner++; break;
            case "lock": f.Session.IsUnlocked = false; break;
            case "private": f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); break;
            case "privacy-state": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { SessionState = WindowsSessionState.Locked }; break;
            case "topology": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 }; break;
            case "call": await f.ViewModel.EnableManualCallCommand.ExecuteAsync(); break;
            case "binding": f.ViewModel.BindConversationShutdown(() => Task.FromResult(true)); break;
            case "validator-binding": f.ViewModel.BindConversationShutdown(prepare, () => false); break;
            case "dispose": f.ViewModel.Dispose(); break;
        }
        decision.SetResult(true);
        await exiting;
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewUnsentStateDuringEditorPromptInvalidatesPreviouslyAcceptedExitOrRestart(bool restart)
    {
        var f = await Fixture.CreateInitializedAsync();
        var editor = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = true;
        f.ViewModel.BindConversationShutdown(() => Task.FromResult(true), () => current);
        f.ViewModel.BindInstructionEditorShutdown(() => { opened.SetResult(); return editor.Task; });
        var exit = restart ? f.RunAsync("restart the app") : f.ViewModel.ExitAsync();
        await opened.Task;
        current = false;
        editor.SetResult(true);
        await exit;
        f.WindowActions.Should().NotContain(WindowAction.Close);
        f.Events.Should().NotContain("process.restart");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateBufferChangeDuringAudioQuiescencePreventsVoluntaryExitOrRestart(bool restart)
    {
        var f = await Fixture.CreateInitializedAsync();
        var current = true;
        f.ViewModel.BindConversationShutdown(() => Task.FromResult(true), () => current);
        f.TextToSpeech.BeforeStop = () => current = false;
        if (restart) { await f.RunAsync("restart the app"); }
        else { await f.ViewModel.ExitAsync(); }
        f.WindowActions.Should().NotContain(WindowAction.Close);
        f.Events.Should().NotContain("process.restart");
        f.ViewModel.CanRevealPrivatePresentation.Should().BeTrue();
    }

    [Fact]
    public async Task PendingVoluntaryDecisionDoesNotDelayMandatoryPrivacyRetirement()
    {
        var f = await Fixture.CreateInitializedAsync();
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ViewModel.BindConversationShutdown(() => decision.Task);
        var exiting = f.ViewModel.ExitAsync();
        f.ViewModel.CloseForObservedPrivacyEvent("mandatory privacy closure", true);
        await f.ViewModel.PrivacyClosureTask;
        f.ViewModel.CanRevealPrivatePresentation.Should().BeFalse();
        exiting.IsCompleted.Should().BeFalse();
        decision.SetResult(true);
        await exiting;
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartClosesInputAtDispatchAndRetainsUncertaintyWhenTerminalAuditFails(bool terminalAuditFailure)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindConversationShutdown(() => Task.FromResult(true), () => true);
        if (terminalAuditFailure)
        {
            f.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, "application.restart", StringComparison.Ordinal) && audit.Outcome == SecurityAuditOutcome.Succeeded)
                {
                    f.ViewModel.CanRevealPrivatePresentation.Should().BeFalse();
                    throw new InvalidOperationException("Synthetic terminal audit failure after dispatch.");
                }
            };
        }
        else { f.Process.RestartException = new("Synthetic restart admission refusal."); }
        await f.RunAsync("restart the app");
        f.WindowActions.Should().NotContain(WindowAction.Close);
        if (terminalAuditFailure) { f.ViewModel.CanRevealPrivatePresentation.Should().BeFalse(); }
        else { f.ViewModel.CanRevealPrivatePresentation.Should().BeTrue(); }
    }
}
