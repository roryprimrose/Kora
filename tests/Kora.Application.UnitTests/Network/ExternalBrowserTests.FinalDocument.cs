using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network.ExternalBrowser;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Network;

public sealed partial class ExternalBrowserTests
{
    private const string FinalLink = "[original site](https://example.com/exact?q=generated)";
    private const string FinalAddress = "https://example.com/exact?q=generated";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FinalSourceReviewIsDistinctFromRetrievalAndRequiresOneShotSeparateConfirmation(bool retained)
    {
        using var root = Root();
        var (state, source) = FinalSource(retained);
        var request = source.CaptureNavigation(0, FinalLink.Length, FinalAddress);
        var launch = new Launcher();
        var audit = new Audit();
        var workflow = Create(launch, audit: audit);
        var review = workflow.Prepare(request, static () => true);
        review.FinalSource.Should().BeSameAs(request);
        review.FinalSource.Source.Reference.Should().Be(state.Reference);
        request.SourceToken.Should().Be(FinalLink);
        request.SourceTitle.Should().Be("Exact final answer");
        request.SourceStart.Should().Be(0);
        request.UserAction.Should().Be(root.Request);
        source.SourceGeneration.Value.Should().Be(retained ? 7 : state.Reference.Revision);
        source.RenderGeneration.Should().Be(state.Generation);
        source.RenderToken.Should().NotBeEmpty();
        source.OriginalSource.Should().Be(retained
            ? new(state.Content!.ConversationMessage!.Request.SessionId, state.Content.ConversationMessage.Request.RequestId,
                state.Content.ConversationMessage.Request.TaskId) : state.Content!.SessionSource);
        source.Origin.Should().Be(state.Content!.Origin);
        state.Content.WebResult.Should().BeNull();
        state.Content.Origin.Should().Be(retained ? DetailContentOrigin.RetainedAssistant : DetailContentOrigin.FinalizedResponse);
        launch.Calls.Should().Be(0);
        workflow.IsCurrent(review).Should().BeTrue();
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Accepted);
        launch.Address.Should().Be(FinalAddress);
        request.IsCurrent.Should().BeFalse();
        request.SourceToken.Should().BeEmpty();
        request.SourceTitle.Should().BeEmpty();
        request.Destination.Should().BeEmpty();
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(1);
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
        audit.Events.Should().OnlyContain(item => item.TargetId == "network.external-browser");
    }

    [Theory]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("rerender")]
    [InlineData("retire")]
    [InlineData("retire-id")]
    [InlineData("request")]
    [InlineData("replacement")]
    [InlineData("expiry")]
    [InlineData("network")]
    [InlineData("host")]
    [InlineData("token")]
    [InlineData("wrong-id")]
    public async Task FinalReviewWithdrawalAndLateReplayNeverLaunchOrAuditSuccess(string closure)
    {
        using var root = Root();
        var admitted = true;
        var (state, source) = FinalSource(current: () => admitted);
        var request = source.CaptureNavigation(0, FinalLink.Length, FinalAddress);
        var launch = new Launcher();
        var audit = new Audit();
        var clock = new Clock();
        var network = new Network();
        var workflow = Create(launch, network, audit, clock);
        var review = workflow.Prepare(request, static () => true);
        using var cancellation = new CancellationTokenSource();
        switch (closure)
        {
            case "close": state.Close(); break;
            case "private": admitted = false; break;
            case "rerender": state.CompleteRender(state.Generation, "replacement projection", "Rendered", false); break;
            case "retire": workflow.Retire(); break;
            case "retire-id": workflow.Retire(review.Id); break;
            case "request": request.Retire(); break;
            case "replacement": workflow.Prepare(source.CaptureNavigation(0, FinalLink.Length, FinalAddress), static () => true); break;
            case "expiry": clock.Now = clock.Now.AddMinutes(2); break;
            case "network": network.Current = false; break;
            case "token": await cancellation.CancelAsync(); break;
        }
        workflow.Retire(Guid.NewGuid());
        (await workflow.ConfirmAsync(closure is "wrong-id" ? Guid.NewGuid() : review.Id,
            () => closure is not "host", cancellation.Token)).Should().Be(
                closure is "token" ? ExternalBrowserLaunchOutcome.Cancelled : ExternalBrowserLaunchOutcome.Denied);
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(0);
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        workflow.Retire();
    }

    [Theory]
    [InlineData("file:///C:/bad.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.com/%zz")]
    [InlineData("https://localhost/")]
    [InlineData("https://example.com/#fragment")]
    public void FinalModelDestinationsAreUntrustedAndNeverBypassTheCanonicalUriValidator(string address)
    {
        using var root = Root();
        var (_, source) = FinalSource();
        var launch = new Launcher();
        var workflow = Create(launch);
        var request = source.CaptureNavigation(0, FinalLink.Length, address);
        FluentActions.Invoking(() => workflow.Prepare(request, static () => true)).Should().Throw<InvalidOperationException>();
        launch.Calls.Should().Be(0);
        request.Retire();
    }

    [Fact]
    public void FinalRequestCannotBorrowAnotherOriginalActionOrBeReusedAndSourceAndNetworkDenialFailClosed()
    {
        var (_, source) = FinalSource();
        var launch = new Launcher();
        var workflow = Create(launch);
        using var root = Root();
        var request = source.CaptureNavigation(0, FinalLink.Length, FinalAddress);
        using (var other = Root())
        { FluentActions.Invoking(() => workflow.Prepare(request, static () => true)).Should().Throw<InvalidOperationException>(); }
        var review = workflow.Prepare(request, static () => true);
        workflow.Retire(Guid.NewGuid());
        workflow.IsCurrent(review).Should().BeTrue();
        FluentActions.Invoking(() => workflow.Prepare(request, static () => true)).Should().Throw<InvalidOperationException>();
        var (state, another) = FinalSource();
        var stale = another.CaptureNavigation(0, FinalLink.Length, FinalAddress);
        state.Close();
        FluentActions.Invoking(() => workflow.Prepare(stale, static () => true)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => workflow.Prepare(source.CaptureNavigation(0, FinalLink.Length, FinalAddress), static () => false))
            .Should().Throw<InvalidOperationException>();
        var offline = Create(launch, new Network { Allowed = false });
        FluentActions.Invoking(() => offline.Prepare(source.CaptureNavigation(0, FinalLink.Length, FinalAddress), static () => true))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => workflow.Prepare((FinalDocumentNavigationRequest)null!, static () => true)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => workflow.Prepare(source.CaptureNavigation(0, FinalLink.Length, FinalAddress), null!)).Should().Throw<ArgumentNullException>();
        launch.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ExternalBrowserLaunchOutcome.Failed, SecurityAuditOutcome.Failed)]
    [InlineData(ExternalBrowserLaunchOutcome.Unknown, SecurityAuditOutcome.Unknown)]
    public async Task FinalLauncherFailureIsTypedAndOwnedLateCompletionMustActuallyJoin(
        ExternalBrowserLaunchOutcome outcome, SecurityAuditOutcome terminal)
    {
        using var root = Root();
        var (state, source) = FinalSource();
        var launch = new Launcher { Result = outcome, Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var audit = new Audit();
        var workflow = Create(launch, audit: audit);
        var review = workflow.Prepare(source.CaptureNavigation(0, FinalLink.Length, FinalAddress), static () => true);
        var pending = workflow.ConfirmAsync(review.Id, static () => true, Token);
        await launch.Entered.Task;
        FluentActions.Invoking(() => workflow.Prepare(source.CaptureNavigation(0, FinalLink.Length, FinalAddress), static () => true))
            .Should().Throw<InvalidOperationException>();
        state.Close();
        var join = workflow.RetireAndWaitAsync();
        join.IsCompleted.Should().BeFalse();
        pending.IsCompleted.Should().BeFalse();
        launch.Release.SetResult();
        (await pending).Should().Be(outcome);
        await join;
        audit.Events[^1].Outcome.Should().Be(terminal);
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        review.FinalSource!.SourceToken.Should().BeEmpty();
    }

    private static (DetailViewerState State, FinalDocumentSource Source) FinalSource(bool retained = true, Func<bool>? current = null)
    {
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var message = new ConversationMessage(request, new(7), 1, 3, DateTimeOffset.UnixEpoch,
            "Original USER question", ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, FinalLink),
        };
        var content = new AdmittedDetailContent(new(new(provenance.Turn.Value), 3), DetailContentKind.Markdown,
            retained ? DetailContentOrigin.RetainedAssistant : DetailContentOrigin.FinalizedResponse,
            DetailSensitivity.DisclosureConfirmationRequired, "Exact final answer", "Untrusted generated final answer", FinalLink,
            sessionSource: retained ? null : new(request.SessionId, request.RequestId, request.TaskId),
            conversationMessage: retained ? message : null);
        var state = new DetailViewerState(content, current ?? (() => true));
        state.CompleteRender(state.Generation, FinalLink, "Rendered", false).Should().BeTrue();
        return (state, state.CaptureFinalDocument());
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    [InlineData("source-cancelled")]
    [InlineData("withdrawn")]
    public async Task FinalConfirmationFreshlyRevalidatesSourceAndJoinsIgnoredCancellationBeforeAnyLauncher(string change)
    {
        using var root = Root();
        var (original, _) = FinalSource();
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new DetailViewerState(original.Content!, () => true, validateSource: async _ =>
        {
            calls++;
            if (calls == 1) { return true; }
            entered.SetResult();
            return await release.Task;
        });
        state.CompleteRender(state.Generation, FinalLink, "Rendered", false);
        var source = state.CaptureFinalDocument();
        var request = await source.CaptureNavigationAsync(0, FinalLink.Length, FinalAddress, Token);
        var launch = new Launcher();
        var audit = new Audit();
        var workflow = Create(launch, audit: audit);
        var review = workflow.Prepare(request, () => true);
        using var cancellation = new CancellationTokenSource();
        var confirmation = workflow.ConfirmAsync(review.Id, () => true, cancellation.Token);
        await entered.Task;
        launch.Calls.Should().Be(0);
        if (change is "cancelled") { await cancellation.CancelAsync(); }
        if (change is "withdrawn") { state.Close(); }
        var join = workflow.RetireAndWaitAsync();
        confirmation.IsCompleted.Should().BeFalse();
        join.IsCompleted.Should().BeFalse();
        if (change is "failed") { release.SetException(new IOException("Synthetic authoritative source failure.")); }
        else if (change is "source-cancelled") { release.SetException(new OperationCanceledException("Synthetic source closure.")); }
        else { release.SetResult(change is not "denied"); }
        if (change is "failed")
        { await FluentActions.Awaiting(() => confirmation).Should().ThrowAsync<IOException>(); }
        else if (change is "source-cancelled")
        { await FluentActions.Awaiting(() => confirmation).Should().ThrowAsync<OperationCanceledException>(); }
        else
        { (await confirmation).Should().Be(change is "cancelled" ? ExternalBrowserLaunchOutcome.Cancelled : ExternalBrowserLaunchOutcome.Denied); }
        await join;
        launch.Calls.Should().Be(0);
        calls.Should().Be(2);
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        request.SourceToken.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedReviewReplacementAndScopedRetirementPreserveBothSourceKindsWithoutLeakingPendingFinalTokens()
    {
        using var root = Root();
        var (_, source) = FinalSource();
        var workflow = Create(new());
        var first = source.CaptureNavigation(0, FinalLink.Length, FinalAddress);
        workflow.Prepare(first, () => true);
        await workflow.RetireAndWaitAsync();
        first.SourceToken.Should().BeEmpty();
        first = source.CaptureNavigation(0, FinalLink.Length, FinalAddress);
        workflow.Prepare(first, () => true);
        var web = workflow.Prepare(Snapshot(), () => true);
        first.SourceToken.Should().BeEmpty();
        workflow.Retire(web.Id);
        workflow.IsCurrent(web).Should().BeFalse();
        web = workflow.Prepare(Snapshot(), () => true);
        await workflow.RetireAndWaitAsync();
        workflow.IsCurrent(web).Should().BeFalse();
        workflow.Prepare(Snapshot(), () => true);
        var final = workflow.Prepare(source.CaptureNavigation(0, FinalLink.Length, FinalAddress), () => true);
        (await workflow.ConfirmAsync(Guid.NewGuid(), () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        final.FinalSource!.SourceToken.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelledAuthoritativeFinalValidationIsTypedCancelledAndDoesNotLaunch()
    {
        using var root = Root();
        var (original, _) = FinalSource();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var state = new DetailViewerState(original.Content!, () => true, validateSource: async _ =>
        {
            calls++;
            if (calls == 1) { return true; }
            await cancellation.CancelAsync();
            throw new OperationCanceledException(cancellation.Token);
        });
        state.CompleteRender(state.Generation, FinalLink, "Rendered", false);
        var request = await state.CaptureFinalDocument().CaptureNavigationAsync(0, FinalLink.Length, FinalAddress, Token);
        var launch = new Launcher();
        var audit = new Audit();
        var workflow = Create(launch, audit: audit);
        var review = workflow.Prepare(request, () => true);
        (await workflow.ConfirmAsync(review.Id, () => true, cancellation.Token)).Should().Be(ExternalBrowserLaunchOutcome.Cancelled);
        launch.Calls.Should().Be(0);
        audit.Events[^1].Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
    }
}
