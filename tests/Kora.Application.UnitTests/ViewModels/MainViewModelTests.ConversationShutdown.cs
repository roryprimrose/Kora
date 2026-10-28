using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NewUnsentInputWhileJoiningExistingRunOwnersPreventsExitAfterSubmittedWorkHasStopped()
    {
        var f = await Fixture.CreateInitializedAsync();
        var current = true;
        var joined = false;
        f.ViewModel.BindConversationShutdown(() => Task.FromResult(true), () => current);
        f.ViewModel.BindSubmittedWorkShutdown((_, _, admitted) =>
            Task.FromResult<ApplicationLifecycleShutdown?>(new(admitted, () => { joined = true; return Task.CompletedTask; }, () => { })));
        f.ViewModel.BindAgentShutdown(() =>
        {
            joined.Should().BeTrue();
            current = false;
            return Task.CompletedTask;
        });
        await f.ViewModel.ExitAsync();
        f.WindowActions.Should().NotContain(WindowAction.Close);
        f.ViewModel.CanRevealPrivatePresentation.Should().BeTrue();
    }

    [Fact]
    public async Task ASecondTypedRestartCannotEnterAnAlreadyPendingVoluntaryExitDecision()
    {
        var f = await Fixture.CreateInitializedAsync();
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ViewModel.BindConversationShutdown(() => decision.Task);
        var exiting = f.ViewModel.ExitAsync();
        await f.RunAsync("restart the app");
        f.Events.Should().NotContain("process.restart");
        decision.SetResult(false);
        await exiting;
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VoluntaryLifecycleJoinsSubmittedWorkBeforeTheActualApplicationEffectExactlyOnce(bool restart)
    {
        var f = await Fixture.CreateInitializedAsync();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = 0;
        var stops = 0;
        f.ViewModel.BindSubmittedWorkShutdown((operation, origin, current) =>
        {
            operation.Should().Be(restart ? ApplicationLifecycleOperation.Restart : ApplicationLifecycleOperation.Exit);
            origin.Should().Be(Kora.Core.Hosting.RequestOrigin.LocalUi);
            return Task.FromResult<ApplicationLifecycleShutdown?>(new(current, () =>
            {
                stops++;
                stopping.SetResult();
                return joined.Task;
            }, () => releases++));
        });
        var operation = restart ? f.RunAsync("restart the app") : f.ViewModel.ExitAsync();
        await stopping.Task;
        await f.ViewModel.ExitAsync();
        stops.Should().Be(1);
        releases.Should().Be(0);
        f.WindowActions.Should().NotContain(WindowAction.Close);
        f.Events.Should().NotContain("process.restart");
        joined.SetResult();
        await operation;
        f.WindowActions.Count(action => action == WindowAction.Close).Should().Be(1);
        f.Events.Count(item => string.Equals(item, "process.restart", StringComparison.Ordinal)).Should().Be(restart ? 1 : 0);
        releases.Should().Be(1);
    }

    [Theory]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "review-failure")]
    [InlineData(true, "review-failure")]
    [InlineData(false, "join-failure")]
    [InlineData(true, "join-failure")]
    [InlineData(false, "changed-before-join")]
    [InlineData(true, "changed-before-join")]
    [InlineData(false, "changed-after-join")]
    [InlineData(true, "changed-after-join")]
    [InlineData(false, "editor-cancel")]
    [InlineData(true, "editor-cancel")]
    public async Task SubmittedWorkFailureOrCancelledEditorReleasesOnlyItsAttemptWithoutAnApplicationEffect(bool restart, string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        var released = 0;
        var stopped = 0;
        var current = true;
        f.ViewModel.BindSubmittedWorkShutdown((_, _, _) =>
        {
            if (fault is "review-failure") { throw new IOException("Synthetic review failure."); }
            return Task.FromResult<ApplicationLifecycleShutdown?>(fault is "cancel" ? null : new(() => current, () =>
            {
                stopped++;
                if (fault is "join-failure") { throw new IOException("Synthetic physical join failure."); }
                if (fault is "changed-after-join") { current = false; }
                return Task.CompletedTask;
            }, () => released++));
        });
        f.ViewModel.BindInstructionEditorShutdown(() =>
        {
            if (fault is "changed-before-join") { current = false; }
            return Task.FromResult(fault is not "editor-cancel");
        });
        if (restart) { await f.RunAsync("restart the app"); }
        else { await f.ViewModel.ExitAsync(); }
        f.WindowActions.Should().NotContain(WindowAction.Close);
        f.Events.Should().NotContain("process.restart");
        released.Should().Be(fault is "cancel" or "review-failure" ? 0 : 1);
        stopped.Should().Be(fault is "join-failure" or "changed-after-join" ? 1 : 0);
        f.ViewModel.BindSubmittedWorkShutdown(null);
        f.ViewModel.BindInstructionEditorShutdown(null);
        await f.ViewModel.ExitAsync();
        f.WindowActions.Should().Contain(WindowAction.Close, "a cancelled or failed attempt cannot latch admission closed");
    }

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
