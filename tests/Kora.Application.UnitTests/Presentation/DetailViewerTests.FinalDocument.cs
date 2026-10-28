using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Presentation;

public sealed partial class DetailViewerTests
{
    [Theory]
    [InlineData("embedded")]
    [InlineData("plain")]
    [InlineData("unadmitted")]
    [InlineData("rendering")]
    [InlineData("fallback")]
    [InlineData("closed")]
    public void OnlyExactHostAdmittedRenderedFinalMarkdownCanProduceInteractionSource(string unavailable)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 4),
            unavailable is "plain" ? DetailContentKind.PlainText : DetailContentKind.Markdown,
            unavailable is "embedded" ? DetailContentOrigin.EmbeddedDocument : DetailContentOrigin.FinalizedResponse,
            DetailSensitivity.DisclosureConfirmationRequired, "Final answer", "Exact source", "[site](https://example.com/)",
            unavailable is "embedded" ? null : new(request.SessionId, request.RequestId, request.TaskId));
        var state = new Kora.Application.Presentation.DetailViewerState(content,
            unavailable is "unadmitted" ? null : () => true);
        if (unavailable is not "rendering") { state.CompleteRender(state.Generation, "semantic", "Rendered", unavailable is "fallback"); }
        if (unavailable is "closed") { state.Close(); }
        state.CanInteractWithFinalDocument.Should().BeFalse();
        FluentActions.Invoking(() => state.CaptureFinalDocument()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(2, int.MaxValue)]
    [InlineData(int.MaxValue, 2)]
    public void FinalNavigationRequiresAnExactCurrentSourceTokenRange(int start, int length)
    {
        using var listener = ListenForFinalActivity();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var state = FinalState();
        FluentActions.Invoking(() => state.CaptureFinalDocument().CaptureNavigation(start, length, "https://example.com/"))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public void FinalContentAndOriginalVoiceSourceCannotImpersonateFreshNativeNavigation(RequestOrigin origin)
    {
        using var listener = ListenForFinalActivity();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        FluentActions.Invoking(() => FinalState().CaptureFinalDocument().CaptureNavigation(0, 1, "https://example.com/"))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FinalReadTokenRetiresOnRerenderGenerationCloseAndPrivateWithdrawalWithoutRevival()
    {
        using var listener = ListenForFinalActivity();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var allowed = true;
        var state = FinalState(() => allowed);
        var source = state.CaptureFinalDocument();
        source.IsCurrent.Should().BeTrue();
        state.CompleteRender(state.Generation + 1, "wrong generation", "Rendered", false).Should().BeFalse();
        source.IsCurrent.Should().BeTrue();
        state.CompleteRender(state.Generation, "same source; new render", "Rendered", false);
        source.IsCurrent.Should().BeFalse();
        FluentActions.Invoking(() => source.CaptureNavigation(0, 1, "https://example.com/")).Should().Throw<InvalidOperationException>();
        var replacement = state.CaptureFinalDocument();
        replacement.RenderToken.Should().NotBe(source.RenderToken);
        allowed = false;
        replacement.IsCurrent.Should().BeFalse();
        allowed = true;
        replacement.IsCurrent.Should().BeFalse();
        state.ActiveText.Should().BeEmpty();
        state.Close();
        source.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public void CompletedUserActionNullDestinationAndRetiredSourceCannotProduceNavigation()
    {
        using var listener = ListenForFinalActivity();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var state = FinalState();
        var source = state.CaptureFinalDocument();
        FluentActions.Invoking(() => source.CaptureNavigation(0, 1, null!)).Should().Throw<ArgumentNullException>();
        state.Close();
        FluentActions.Invoking(() => source.CaptureNavigation(0, 1, "https://example.com/")).Should().Throw<InvalidOperationException>();
        root.Complete(HostOperationOutcome.Completed);
        FluentActions.Invoking(() => FinalState().CaptureFinalDocument().CaptureNavigation(0, 1, "https://example.com/"))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("cancel")]
    [InlineData("deny")]
    [InlineData("rerender")]
    public async Task RequiredAsynchronousSourceValidationCannotPublishAnUnjoinedOrWithdrawnNavigation(string withdrawal)
    {
        using var listener = ListenForFinalActivity();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var live = true;
        var state = FinalState(() => live, async _ => { entered.SetResult(); return await release.Task; });
        var source = state.CaptureFinalDocument();
        FluentActions.Invoking(() => source.CaptureNavigation(0, 1, "https://example.com/")).Should().Throw<InvalidOperationException>();
        using var cancellation = new CancellationTokenSource();
        var operation = source.CaptureNavigationAsync(0, 1, "https://example.com/", cancellation.Token);
        await entered.Task;
        operation.IsCompleted.Should().BeFalse();
        if (withdrawal is "close") { state.Close(); }
        if (withdrawal is "private") { live = false; }
        if (withdrawal is "cancel") { await cancellation.CancelAsync(); }
        if (withdrawal is "rerender") { state.CompleteRender(state.Generation, "replacement", "Rendered", false); }
        operation.IsCompleted.Should().BeFalse("source validators that ignore cancellation are still owned until physically returned");
        release.SetResult(withdrawal is not "deny");
        await FluentActions.Awaiting(() => operation).Should().ThrowAsync<InvalidOperationException>();
        if (withdrawal is "deny") { state.Content.Should().BeNull(); }
    }

    [Fact]
    public async Task ValidatedNativeSourceProducesOneExactRequestAndCancellationCannotReadAgain()
    {
        using var listener = ListenForFinalActivity();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var calls = 0;
        var state = FinalState(validate: _ => { calls++; return Task.FromResult(true); });
        var source = state.CaptureFinalDocument();
        var request = await source.CaptureNavigationAsync(0, 7, "https://example.com/", TestContext.Current.CancellationToken);
        request.SourceToken.Should().Be("literal");
        request.UserAction.Should().Be(root.Request);
        calls.Should().Be(1);
        (await source.ValidateAsync(new CancellationToken(true))).Should().BeFalse();
        calls.Should().Be(1);
        state.Close();
        (await source.ValidateAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        calls.Should().Be(1);
        request.Retire();
    }

    [Fact]
    public void StoppedNativeActionCannotCreateFinalNavigationEvenBeforeItsAmbientScopeIsUnwound()
    {
        using var listener = ListenForFinalActivity();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var source = FinalState().CaptureFinalDocument();
        var observed = false;
        listener.ActivityStopped = activity =>
        {
            if (!ReferenceEquals(activity, root.Activity)) { return; }
            observed = true;
            FluentActions.Invoking(() => source.CaptureNavigation(0, 1, "https://example.com/")).Should().Throw<InvalidOperationException>();
        };
        root.Activity!.Stop();
        observed.Should().BeTrue();
    }

    private static ActivityListener ListenForFinalActivity()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static Kora.Application.Presentation.DetailViewerState FinalState(Func<bool>? admitted = null,
        Func<CancellationToken, Task<bool>>? validate = null)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
            "Final answer", "Exact frozen final source", "literal final source",
            new(request.SessionId, request.RequestId, request.TaskId));
        var state = new Kora.Application.Presentation.DetailViewerState(content, admitted ?? (() => true), validateSource: validate);
        state.CompleteRender(state.Generation, "semantic", "Rendered", false);
        return state;
    }
}
