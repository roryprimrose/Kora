using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.Voice;
using Kora.Core.Customizations;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Definitions;

[Collection("Host tracing")]
public sealed class CustomizationVoiceClarificationTests
{
    [Fact]
    public void Other_input_channels_cannot_be_relabelled_as_editor_voice()
    {
        var target = new CustomizationVoiceTarget(new(CustomizationKind.Instruction, CustomizationScope.Kora,
            "example", "example.instructions.md", false, false), static () => true);
        Action create = () => _ = new CustomizationVoiceRequest(target, "original",
            HostRequest.Create(RequestOrigin.LocalUi), static () => true, static () => 0);
        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Refine this artifact", CustomizationVoiceChoice.Refine)]
    [InlineData(" discuss this artifact ", CustomizationVoiceChoice.Discuss)]
    [InlineData("Cancel", CustomizationVoiceChoice.Cancel)]
    [InlineData("option 1", CustomizationVoiceChoice.Refine)]
    [InlineData("2", CustomizationVoiceChoice.Discuss)]
    [InlineData("save and enable", CustomizationVoiceChoice.Cancel)]
    public async Task Spoken_choice_consumes_only_original_question_and_joins_before_continuation(string text, CustomizationVoiceChoice expected)
    {
        await using var f = new Fixture();
        var changes = 0;
        f.Question.Changed += (_, _) => changes++;
        f.Question.CanAnswerByVoice.Should().BeTrue();
        f.Question.StartVoice();
        var generation = f.Capture.Generation;
        f.Request.IsCurrent.Should().BeFalse("the original operation cannot run while capture is handed over");
        f.Question.CanAnswerByVoice.Should().BeFalse();
        f.Capture.Fail(generation - 1);
        f.Capture.Complete(generation - 1);
        f.Capture.Transcript("Refine this artifact", generation - 1);
        f.Question.Completion.IsCompleted.Should().BeFalse();
        f.Capture.Transcript(text);
        f.Capture.Transcript("Discuss this artifact", generation);
        var choice = await f.Question.Completion;
        choice.Should().Be(expected);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Stops.Should().Be(1);
        f.Request.IsCurrent.Should().Be(expected != CustomizationVoiceChoice.Cancel);
        f.Question.Choose(CustomizationVoiceChoice.Discuss);
        f.Capture.Fail(generation - 1);
        f.Capture.Complete(generation - 1);
        (await f.Question.Completion).Should().Be(expected);
        changes.Should().BeGreaterThan(0);
        f.Question.Status.Should().NotContain("save and enable");
    }

    [Theory]
    [InlineData(CustomizationVoiceChoice.Refine)]
    [InlineData(CustomizationVoiceChoice.Discuss)]
    [InlineData(CustomizationVoiceChoice.Cancel)]
    public async Task Native_choice_uses_same_exact_question_without_touching_other_capture(CustomizationVoiceChoice choice)
    {
        await using var f = new Fixture();
        f.Question.Choose(choice);
        (await f.Question.Completion).Should().Be(choice);
        f.Capture.Opens.Should().Be(0);
        f.Capture.Stops.Should().Be(0);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("exact")]
    [InlineData("authority")]
    [InlineData("generation")]
    [InlineData("admission")]
    [InlineData("expiry")]
    [InlineData("cancellation")]
    [InlineData("failed")]
    [InlineData("completed")]
    public async Task Withdrawn_question_and_late_reply_have_no_continuation(string change)
    {
        await using var f = new Fixture();
        f.Question.StartVoice();
        var generation = f.Capture.Generation;
        switch (change)
        {
            case "target": f.Request.Target.Retire(); break;
            case "exact": f.Exact = false; break;
            case "authority": f.Admitted = false; break;
            case "generation": f.Capture.AdvanceGeneration(); break;
            case "admission": f.Admission = null; break;
            case "expiry": f.Time.Advance(TimeSpan.FromSeconds(75)); break;
            case "cancellation": f.Cancellation.Cancel(); break;
            case "failed": f.Capture.Fail(); break;
            case "completed": f.Capture.Complete(); break;
        }
        f.Question.Revalidate();
        f.Capture.Transcript("Refine this artifact", generation);
        (await f.Question.Completion).Should().Be(CustomizationVoiceChoice.Cancel);
        f.Request.IsCurrent.Should().BeFalse();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing-capture")]
    [InlineData("missing-admission")]
    [InlineData("busy-capture")]
    [InlineData("retired-target")]
    [InlineData("stale-generation")]
    public async Task Unavailable_voice_never_opens_and_preserves_native_choice(string state)
    {
        await using var f = new Fixture(withCapture: state is not "missing-capture");
        if (state is "missing-admission") { f.Admission = null; }
        if (state is "busy-capture") { f.Capture.NonQuiescent = true; }
        if (state is "retired-target") { f.Request.Target.Retire(); }
        if (state is "stale-generation") { f.Capture.AdvanceGeneration(); }
        f.Question.StartVoice();
        f.Question.Status.Should().Contain("unavailable");
        f.Question.Choose(CustomizationVoiceChoice.Discuss);
        (await f.Question.Completion).Should().Be(state is "retired-target" or "stale-generation"
            ? CustomizationVoiceChoice.Cancel : CustomizationVoiceChoice.Discuss);
        f.Capture.Opens.Should().Be(0);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("ack")]
    [InlineData("revoked-open")]
    [InlineData("handover")]
    public async Task Opening_failure_retires_exact_request_and_joins_capture(string failure)
    {
        await using var f = new Fixture();
        if (failure is "open") { f.Capture.OpenFailure = new IOException("Synthetic private opening failure"); }
        if (failure is "ack") { f.Capture.RejectAcknowledgement = true; }
        if (failure is "revoked-open") { f.Capture.BeforeOpen = () => f.Admitted = false; }
        if (failure is "handover") { f.BeforeObserve = () => f.Capture.AdvanceGeneration(); }
        f.Question.StartVoice();
        (await f.Question.Completion).Should().Be(CustomizationVoiceChoice.Cancel);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Request.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Disposal_cancels_and_joins_inflight_open_and_release()
    {
        var f = new Fixture();
        f.Capture.Opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Question.StartVoice();
        var disposal = f.Question.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse();
        f.Capture.Closing.SetResult();
        await disposal;
        f.Capture.Transcript("Refine this artifact");
        f.Request.IsCurrent.Should().BeFalse();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Time.Timers.Should().OnlyContain(timer => timer.Disposed);
        f.Cancellation.Dispose();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("non-quiescent")]
    [InlineData("foreign-generation")]
    public async Task Unconfirmed_cleanup_faults_without_continuation(string failure)
    {
        var f = new Fixture();
        f.Question.StartVoice();
        if (failure is "failure") { f.Capture.StopFailure = new IOException("Synthetic release failure"); }
        if (failure is "non-quiescent") { f.Capture.NonQuiescent = true; }
        if (failure is "foreign-generation")
        {
            f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Capture.StoppedGenerationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        f.Capture.Transcript("Refine this artifact");
        if (failure is "foreign-generation")
        {
            await f.Capture.StoppedGenerationObserved!.Task.WaitAsync(TestContext.Current.CancellationToken);
            f.Capture.AdvanceGeneration();
            f.Capture.Closing!.SetResult();
        }
        await ((Func<Task>)(() => f.Question.Completion)).Should().ThrowAsync<Exception>();
        f.Request.IsCurrent.Should().BeFalse();
        f.Question.Status.Should().Contain("cleanup unconfirmed");
        await ((Func<Task>)(async () => await f.DisposeAsync())).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Invalid_native_choice_and_stale_native_question_cancel_without_effects()
    {
        await using var f = new Fixture();
        f.Question.Choose((CustomizationVoiceChoice)99);
        (await f.Question.Completion).Should().Be(CustomizationVoiceChoice.Cancel);
        f.Capture.Opens.Should().Be(0);
    }

    [Fact]
    public async Task Native_question_ignores_unowned_events_and_rejects_foreign_generation()
    {
        await using var f = new Fixture();
        f.Capture.Fail();
        f.Capture.Complete();
        f.Capture.Transcript("Discuss this artifact");
        f.Capture.InvalidateCapture();
        f.Question.Revalidate();
        (await f.Question.Completion).Should().Be(CustomizationVoiceChoice.Cancel);
        f.Capture.Stops.Should().Be(0);
    }

    [Fact]
    public async Task Capture_observation_is_exact_across_opening_and_consumption()
    {
        await using var f = new Fixture();
        f.Capture.BeforeOpen = () => { f.Capture.InvalidateCapture(); f.Question.Revalidate(); };
        f.Question.StartVoice();
        f.Capture.PreserveGenerationOnInvalidate = true;
        f.Capture.InvalidateCapture();
        f.Question.Revalidate();
        f.Question.Completion.IsCompleted.Should().BeFalse();
        f.Capture.PreserveGenerationOnInvalidate = false;
        f.Capture.InvalidateCapture();
        (await f.Question.Completion).Should().Be(CustomizationVoiceChoice.Cancel);
        f.Request.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Consumed_question_cannot_revoke_original_continuation_with_late_events()
    {
        await using var f = new Fixture();
        f.Question.Revalidate();
        f.Question.StartVoice();
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Transcript("Refine this artifact");
        await f.Capture.StopStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.Capture.PreserveGenerationOnInvalidate = true;
        f.Capture.InvalidateCapture();
        f.Question.Revalidate();
        f.Capture.Fail();
        f.Capture.Complete();
        f.Capture.Closing.SetResult();
        (await f.Question.Completion).Should().Be(CustomizationVoiceChoice.Refine);
        f.Question.Cancel("Late withdrawn observer");
        f.Question.Revalidate();
        f.Request.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Tracing_links_only_original_voice_request_without_content()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (stopped) { stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);
        using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        await using var f = new Fixture();
        f.Question.StartVoice();
        f.Capture.Transcript("Discuss this artifact");
        await f.Question.Completion;
        stopped.Should().Contain(activity => activity.Links.Any());
        stopped.SelectMany(activity => activity.TagObjects).Select(tag => tag.Value).Should().NotContain("Discuss this artifact");
        original.Complete(HostOperationOutcome.Completed);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public QuestionReplyFixture.ReplyCapture Capture { get; } = new();
        public QuestionReplyFixture.ReplyTime Time { get; } = new();
        public CancellationTokenSource Cancellation { get; } = new();
        public QuestionVoiceAdmission? Admission { get; set; } = new(new("mic", "Synthetic"), 1, 1, 1);
        public bool Exact { get; set; } = true;
        public bool Admitted { get; set; } = true;
        public Action? BeforeObserve { get; set; }
        public CustomizationVoiceRequest Request { get; }
        public CustomizationVoiceClarification Question { get; }
        public Fixture(bool withCapture = true)
        {
            Request = new(new(new(CustomizationKind.Instruction, CustomizationScope.Kora, "example", "example.instructions.md", false, false),
                static () => true), "private original request", HostRequest.Create(RequestOrigin.ActivatedVoice), () => Admitted, () => Capture.Generation);
            Question = new(Request, () => Exact, withCapture ? Capture : null,
                () => { BeforeObserve?.Invoke(); return Admission; }, Time,
                NullLogger<CustomizationVoiceClarification>.Instance, Cancellation.Token);
        }
        public async ValueTask DisposeAsync()
        {
            try { await Question.DisposeAsync(); }
            finally { Cancellation.Dispose(); }
        }
    }
}
