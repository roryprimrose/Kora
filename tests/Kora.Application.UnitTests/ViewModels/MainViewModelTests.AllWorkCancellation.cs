using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;

using AllWorkStopOutcome = Kora.Application.Hosting.AllWorkStopResult.OutcomeKind;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAllNeverAnswersOrCancelsAnUnrelatedForegroundQuestionOrGrantBeforeConfirmation(bool grant)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which operation?", ["stop all work", "keep working"]);
        await f.ViewModel.InitializeAsync();
        if (grant)
        {
            await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
                BuiltInAction.LockMachine, ModelApprovalScope.Session));
        }
        else
        {
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
        }
        var calls = 0;
        f.ViewModel.BindAllWorkStop((_, current, _) =>
        {
            current().Should().BeTrue();
            calls++;
            return Task.FromResult(new AllWorkStopResult(AllWorkStopOutcome.Cancelled));
        });
        await f.RunAsync("stop all work");
        calls.Should().Be(1);
        f.ViewModel.IsModelQuestionPending.Should().Be(!grant);
        f.ViewModel.IsGrantChangePending.Should().Be(grant);
        f.ViewModel.SessionAllowedModelActions.Should().NotContain(item => item.Action == BuiltInAction.LockMachine);
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task StopAllUsesTheOriginalUserChannelAndNeverCallsShutdownOrDiscard(RequestOrigin origin)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        var result = new AllWorkStopResult(AllWorkStopOutcome.Completed, [], [], [], true);
        f.ViewModel.BindConversationShutdown(() => throw new InvalidOperationException("No unsent review."));
        f.ViewModel.BindInstructionEditorShutdown(() => throw new InvalidOperationException("No editor discard."));
        f.ViewModel.BindAllWorkStop((actual, admitted, _) =>
        {
            actual.Should().Be(origin);
            admitted().Should().BeTrue();
            calls++;
            f.ViewModel.PresentAllWorkJoining();
            return Task.FromResult(result);
        });
        if (origin == RequestOrigin.ActivatedVoice) { await f.RaiseActivatedTranscriptAsync("Kora, stop all work", 1); }
        else { await f.RunAsync("stop all work"); }
        calls.Should().Be(1);
        f.ViewModel.LastAllWorkStop.Should().BeSameAs(result);
        f.ViewModel.ResponseBody.Should().Be(result.Summary);
        f.WindowActions.Should().NotContain(WindowAction.Close);
        f.Events.Should().NotContain("process.restart");
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task StopAllDeniesModelSuggestionEvenWhenAGenericReusableGrantExists()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.StopAllWork, ModelApprovalScope.Session));
        await f.ViewModel.ConfirmGrantChangeAsync();
        f.ViewModel.SessionAllowedModelActions.Should().Contain(item => item.Action == BuiltInAction.StopAllWork);
        f.ViewModel.BindAllWorkStop((_, _, _) => throw new InvalidOperationException("A model cannot open this review."));
        await f.ViewModel.ExecuteAsync(f.Catalog.GetCommands().Single(item => item.Action == BuiltInAction.StopAllWork),
            SecurityAuditInitiator.ModelSuggestion);
        f.ViewModel.ResponseTitle.Should().Be("Model Stop All is unavailable.");
        f.ViewModel.LastAllWorkStop.Should().BeNull();
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Fact]
    public async Task StopAllRejectsAnActualModelTurnWithAnExistingGrantWithoutSpeakingOrOpeningReview()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Action = BuiltInAction.StopAllWork;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
            BuiltInAction.StopAllWork, ModelApprovalScope.Session));
        await f.ViewModel.ConfirmGrantChangeAsync();
        f.ViewModel.BindAllWorkStop((_, _, _) => throw new InvalidOperationException("No model Stop All review."));
        await f.RunAsync("an original request whose model proposes Stop All");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.ResponseTitle.Should().Be("Model Stop All is unavailable.");
        f.ViewModel.LastAllWorkStop.Should().BeNull();
        f.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("host")]
    [InlineData("cancelled")]
    [InlineData("source")]
    [InlineData("private")]
    public async Task StopAllWithoutNativeOrOriginalPrivateAdmissionIsExplicitlyUnavailable(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (fault is not "missing")
        { f.ViewModel.BindAllWorkStop((_, _, _) => throw new InvalidOperationException("No unauthorized review.")); }
        if (fault is "cancelled") { cancellation.Cancel(); }
        if (fault is "private") { f.ViewModel.BindCallOwnershipGate(() => false); }
        using var root = fault is "host"
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request) : null;
        var result = await f.ViewModel.StopAllWorkAsync(() => fault is not "source", cancellation.Token);
        result.Outcome.Should().Be(AllWorkStopOutcome.Unavailable);
        f.ViewModel.ResponseBody.Should().Contain("No stop was requested");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("binding")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("host")]
    [InlineData("replaced")]
    [InlineData("cancelled")]
    [InlineData("input")]
    public async Task StopAllRevalidatesItsExactSourceAcrossTheNativeDecision(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        var source = true;
        f.ViewModel.BindAllWorkStop((_, current, _) =>
        {
            current().Should().BeTrue();
            switch (fault)
            {
                case "source": source = false; break;
                case "privacy": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 100 }; break;
                case "binding": f.ViewModel.BindCallOwnershipGate(() => true, () => owner); break;
                case "owner": owner++; break;
                case "call": f.CallState.SetState(CallState.Active); break;
                case "host": f.ViewModel.BindCallOwnershipGate(() => false); break;
                case "replaced": f.ViewModel.BindAllWorkStop(null); break;
                case "cancelled": cancellation.Cancel(); break;
                case "input": f.ViewModel.CommandText = "replacement original input"; break;
            }
            current().Should().BeFalse();
            return Task.FromResult(new AllWorkStopResult(AllWorkStopOutcome.Stale));
        });
        (await f.ViewModel.StopAllWorkAsync(() => source, cancellation.Token)).Outcome.Should().Be(AllWorkStopOutcome.Stale);
    }

    [Fact]
    public async Task StopAllOwnsOneAttemptUntilItsJoinAndRetainsAnExplicitUnexpectedFailure()
    {
        var f = await Fixture.CreateInitializedAsync();
        var joined = new TaskCompletionSource<AllWorkStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ViewModel.BindAllWorkStop((_, _, _) => joined.Task);
        var operation = f.ViewModel.StopAllWorkAsync(token: TestContext.Current.CancellationToken);
        (await f.ViewModel.StopAllWorkAsync(token: TestContext.Current.CancellationToken)).Outcome.Should().Be(AllWorkStopOutcome.InProgress);
        operation.IsCompleted.Should().BeFalse();
        joined.SetException(new IOException("Synthetic resource uncertainty."));
        (await operation).Outcome.Should().Be(AllWorkStopOutcome.PartialUnconfirmed);
        f.ViewModel.ResponseTitle.Should().Be("Stop all work unconfirmed.");
        f.ViewModel.BindAllWorkStop((_, _, _) => Task.FromResult(new AllWorkStopResult(AllWorkStopOutcome.NothingToStop)));
        (await f.ViewModel.StopAllWorkAsync(token: TestContext.Current.CancellationToken)).Outcome.Should().Be(AllWorkStopOutcome.NothingToStop);
    }

    [Fact]
    public async Task StopAllCannotRetainAuthorityAcrossAnotherOriginalCommand()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindAllWorkStop(async (_, current, _) =>
        {
            current().Should().BeTrue();
            await f.RunAsync("help");
            current().Should().BeFalse();
            return new(AllWorkStopOutcome.Stale);
        });
        (await f.ViewModel.StopAllWorkAsync(token: TestContext.Current.CancellationToken)).Outcome.Should().Be(AllWorkStopOutcome.Stale);
        f.WindowActions.Should().NotContain(WindowAction.Close);
    }

    [Fact]
    public void StopAllOutcomesAreTypedAndNeverPromiseFutureGlobalCancellation()
    {
        foreach (var outcome in Enum.GetValues<AllWorkStopOutcome>())
        { new AllWorkStopResult(outcome).Summary.Should().NotBeNullOrWhiteSpace(); }
        var invalid = () => new AllWorkStopResult((AllWorkStopOutcome)999).Summary;
        invalid.Should().Throw<InvalidOperationException>();
    }
}
