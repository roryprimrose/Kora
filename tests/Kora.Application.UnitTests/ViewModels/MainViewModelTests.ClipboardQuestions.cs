using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task OriginalNativeQuestionCompletesAgainstSameCaptureWithoutGenericContentPublication()
    {
        var fixture = new Fixture(enableClipboardExplanation: true);
        try
        {
            await fixture.ViewModel.PreviewClipboardAsync();
            var source = fixture.ViewModel.ClipboardPreview!;
            var review = fixture.ViewModel.ReviewClipboardQuestion(source.SnapshotId, "  What does it mean?\t");
            review.Source.Should().BeSameAs(source);
            review.Question.Should().Be("  What does it mean?\t");
            var result = await fixture.ViewModel.AnswerClipboardQuestionAsync(review, TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(ClipboardExplanationOutcome.Succeeded, "model reason: {0}", result.ModelReason);
            fixture.ViewModel.ClipboardExplanation.Should().Be("Genuine synthetic local explanation.");
            fixture.ViewModel.ResponseBody.Should().NotContain(source.Text).And.NotContain(review.Question);
            fixture.ViewModel.Transcript.Should().NotContain(review.Question);
            fixture.ClipboardReader.Calls.Should().Be(1);
            fixture.ViewModel.ClipboardPreview.Should().BeSameAs(source);
            fixture.ViewModel.ClearClipboardPreview();
        }
        finally { fixture.ViewModel.Dispose(); await fixture.ClipboardWorkflowLifetime!.DisposeAsync(); }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("clear")]
    public async Task OriginalQuestionReviewCannotSurviveHostAdmissionChanges(string change)
    {
        var fixture = new Fixture(enableClipboardExplanation: true);
        try
        {
            await fixture.ViewModel.PreviewClipboardAsync();
            var review = fixture.ViewModel.ReviewClipboardQuestion(fixture.ViewModel.ClipboardPreview!.SnapshotId, "Question");
            review.IsCurrent().Should().BeTrue();
            switch (change)
            {
                case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => true); break;
                case "input": await fixture.RunAsync("Kora help"); break;
                case "privacy": fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
                case "call":
                    await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                        fixture.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken); break;
                default: fixture.ViewModel.ClearClipboardPreview(); break;
            }
            review.IsCurrent().Should().BeFalse();
            (await fixture.ViewModel.AnswerClipboardQuestionAsync(review, TestContext.Current.CancellationToken))
                .Outcome.Should().Be(ClipboardExplanationOutcome.Denied);
            fixture.ViewModel.ClipboardExplanation.Should().BeNull();
        }
        finally { fixture.ViewModel.Dispose(); await fixture.ClipboardWorkflowLifetime!.DisposeAsync(); }
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem, false, false, false, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false, false, false, false)]
    [InlineData(RequestOrigin.LocalUi, true, false, false, false)]
    [InlineData(RequestOrigin.LocalUi, false, true, false, false)]
    [InlineData(RequestOrigin.LocalUi, false, false, true, false)]
    [InlineData(RequestOrigin.LocalUi, false, false, true, true)]
    public async Task AmbientOrCompletedInputCannotBeRelabelledAsOriginalQuestion(RequestOrigin origin, bool invocation,
        bool completed, bool stopped, bool restoreActivity)
    {
        var fixture = new Fixture(enableClipboardExplanation: true);
        try
        {
            await fixture.ViewModel.PreviewClipboardAsync();
            var review = fixture.ViewModel.ReviewClipboardQuestion(fixture.ViewModel.ClipboardPreview!.SnapshotId, "Question");
            var request = HostRequest.Create(origin);
            if (invocation) { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            if (completed) { root.Complete(HostOperationOutcome.Completed); }
            if (stopped) { root.Activity!.Stop(); }
            if (restoreActivity)
            {
                System.Diagnostics.Activity.Current = root.Activity;
                HostActivity.Current.Should().BeNull("a stopped activity cannot restore host input authority");
                root.Activity!.IsStopped.Should().BeTrue();
                root.Outcome.Should().Be(HostOperationOutcome.Unknown);
            }
            var prepare = () => fixture.ViewModel.ReviewClipboardQuestion(review.Source.SnapshotId, "Question");
            prepare.Should().Throw<InvalidOperationException>();
            var answer = () => fixture.ViewModel.AnswerClipboardQuestionAsync(review, TestContext.Current.CancellationToken);
            await answer.Should().ThrowAsync<InvalidOperationException>();
        }

        finally { fixture.ViewModel.Dispose(); await fixture.ClipboardWorkflowLifetime!.DisposeAsync(); }
    }

    [Fact]
    public async Task CancelledNativeQuestionReportsCancellationAndJoinsItsWorkflow()
    {
        var fixture = new Fixture(enableClipboardExplanation: true);
        try
        {
            await fixture.ViewModel.PreviewClipboardAsync();
            var review = fixture.ViewModel.ReviewClipboardQuestion(fixture.ViewModel.ClipboardPreview!.SnapshotId, "Question");
            fixture.ClipboardWorkflow!.Changed += Cancel;
            void Cancel(object? sender, EventArgs args)
            {
                fixture.ClipboardWorkflow.Changed -= Cancel;
                fixture.ViewModel.ClearClipboardPreview();
            }
            (await fixture.ViewModel.AnswerClipboardQuestionAsync(review, TestContext.Current.CancellationToken))
                .Outcome.Should().Be(ClipboardExplanationOutcome.Cancelled);
            fixture.ClipboardWorkflow.IsQuiescent.Should().BeTrue();
            fixture.ViewModel.ClipboardExplanation.Should().BeNull();
        }
        finally { fixture.ViewModel.Dispose(); await fixture.ClipboardWorkflowLifetime!.DisposeAsync(); }
    }

    [Fact]
    public async Task UnavailableCompositionRefusesReviewAndConfirmationExplicitly()
    {
        var available = new Fixture(enableClipboardExplanation: true);
        var unavailable = new Fixture();
        try
        {
            await available.ViewModel.PreviewClipboardAsync();
            var review = available.ViewModel.ReviewClipboardQuestion(available.ViewModel.ClipboardPreview!.SnapshotId, "Question");
            var prepare = () => unavailable.ViewModel.ReviewClipboardQuestion(review.Source.SnapshotId, "Question");
            prepare.Should().Throw<InvalidOperationException>();
            var answer = () => unavailable.ViewModel.AnswerClipboardQuestionAsync(review, TestContext.Current.CancellationToken);
            await answer.Should().ThrowAsync<InvalidOperationException>();
            using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            available.ViewModel.ReviewClipboardQuestion(review.Source.SnapshotId, "Another original question").IsCurrent().Should().BeTrue();
        }
        finally
        {
            unavailable.ViewModel.Dispose();
            available.ViewModel.Dispose();
            await available.ClipboardWorkflowLifetime!.DisposeAsync();
        }
    }
}
