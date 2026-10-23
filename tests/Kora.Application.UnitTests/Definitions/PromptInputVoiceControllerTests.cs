using System.Collections.Concurrent;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.UnitTests.Network;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Definitions;

[Collection("Host tracing")]
public sealed class PromptInputVoiceControllerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OneUtteranceReplacesOnlyNamedLiteralAndRequiresNewWholePreview()
    {
        await using var f = new Fixture();
        f.Draft.SetValue("topic", "old");
        f.Draft.SetValue("topic-two", "untouched");
        var previous = await f.Draft.Preview(f.Fresh, Token);
        await f.Begin();
        f.Draft.IsUseCurrent(previous).Should().BeFalse();
        f.Capture.Transcript("  ${input:topic-two}\r\nKora lock workstation  ");
        await f.Controller.Quiescence;
        f.Applied.Should().Be(1);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Controller.IsActive.Should().BeFalse();
        f.Capture.Transcript("late", f.Generation);
        f.Applied.Should().Be(1);
        f.Draft.CanUse.Should().BeFalse();
        (await f.Draft.Preview(f.Fresh, Token)).UserRequest.Should().Be(
            "Request   ${input:topic-two}\r\nKora lock workstation   / untouched");
        f.Controller.Status.Should().NotContain("lock workstation");
    }

    [Theory]
    [InlineData("before-read")]
    [InlineData("stale-source")]
    [InlineData("after-read")]
    public async Task OpeningCannotCaptureAfterExactSourceOrFocusValidationIsWithdrawn(string boundary)
    {
        await using var f = new Fixture();
        if (boundary is "before-read")
        { f.Controller.Changed += (_, _) => f.Focused = false; }
        if (boundary is "stale-source") { f.FreshSource = false; }
        if (boundary is "after-read") { f.BeforeFresh = () => f.Focused = false; }
        await f.Begin();
        await f.Controller.Quiescence;
        f.Applied.Should().Be(0);
        f.Capture.Opens.Should().Be(0);
        f.Controller.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CancellationDuringOpeningNotificationJoinsReleaseBeforeAnExpiryTimerExists()
    {
        await using var f = new Fixture();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.Controller.Changed += (_, _) =>
        {
            if (f.Controller.IsActive) { cancelled.Cancel(); }
        };
        await f.Controller.Begin(f.Draft, "topic", () => f.Focused, f.Apply, cancelled.Token);
        await f.Controller.Quiescence;
        f.Capture.Opens.Should().Be(0);
        f.Applied.Should().Be(0);
        f.Time.Timers.Should().OnlyContain(timer => timer.Disposed);
    }

    [Fact]
    public async Task FailedExpirySetupRetiresTheTargetAndCompletesOwnedRelease()
    {
        await using var f = new Fixture(time: new UnavailableExpiryTime());
        await f.Begin();
        await f.Controller.Quiescence;
        f.Controller.IsActive.Should().BeFalse();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Opens.Should().Be(0);
        f.Applied.Should().Be(0);
        f.Controller.Status.Should().Contain("failed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedNativeReleaseFaultsTheReceiptAndNeverPublishesOrReopens(bool duringDelivery)
    {
        var f = new Fixture();
        try
        {
            await f.Begin();
            f.Capture.NonQuiescent = true;
            if (duringDelivery) { f.Capture.Transcript("private must not publish"); }
            else { f.Controller.Retire("Cancel while native release cannot be confirmed"); }
            await ((Func<Task>)(() => f.Controller.Quiescence)).Should().ThrowAsync<InvalidOperationException>();
            f.Applied.Should().Be(0);
            f.Controller.Status.Should().Contain("cleanup unconfirmed");
            f.Capture.NonQuiescent = false;
            await f.Begin();
            f.Capture.Opens.Should().Be(1);
            f.Logger.Messages.Should().NotBeEmpty().And.NotContain(message => message.Contains("private must not publish", StringComparison.Ordinal));
        }
        finally { await ((Func<Task>)(async () => await f.DisposeAsync())).Should().ThrowAsync<InvalidOperationException>(); }
    }

    [Fact]
    public async Task CleanupObserverFailureFaultsThePublishedReceiptInsteadOfHangingOrReopening()
    {
        var f = new Fixture();
        try
        {
            await f.Begin();
            var observerFailed = false;
            f.Controller.Changed += (_, _) =>
            {
                if (!observerFailed && f.Controller.Status.Contains("cleanup unconfirmed", StringComparison.Ordinal))
                {
                    observerFailed = true;
                    throw new IOException("Synthetic cleanup observer failure");
                }
            };
            f.Capture.StopFailure = new IOException("Synthetic stop failure");
            f.Controller.Retire("Explicit cancellation");
            await ((Func<Task>)(() => f.Controller.Quiescence)).Should().ThrowAsync<IOException>()
                .WithMessage("Synthetic cleanup observer failure");
            observerFailed.Should().BeTrue();
            f.Applied.Should().Be(0);
            f.Capture.StopFailure = null;
            await f.Begin();
            f.Capture.Opens.Should().Be(1);
        }
        finally
        {
            await ((Func<Task>)(async () => await f.DisposeAsync())).Should().ThrowAsync<IOException>()
                .WithMessage("Synthetic cleanup observer failure");
        }
    }

    [Fact]
    public async Task MissingAndForeignCompletionEventsCannotRetireCurrentOrAlreadyDeliveringTarget()
    {
        await using var f = new Fixture();
        f.Capture.Fail();
        f.Capture.Complete();
        await f.Begin();
        f.Capture.Fail(f.Generation - 1);
        f.Capture.Complete(f.Generation - 1);
        f.Controller.IsActive.Should().BeTrue();
        f.Dispatcher.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Transcript("exact current value");
        f.Capture.Complete(f.Generation);
        f.Controller.IsActive.Should().BeTrue();
        f.Dispatcher.Hold.SetResult();
        await f.Controller.Quiescence;
        f.Applied.Should().Be(1);
        f.Capture.Fail(f.Generation);
        f.Capture.Complete(f.Generation);
        f.Applied.Should().Be(1);
    }

    [Fact]
    public async Task ForeignHostActivityAtPublicationCannotAuthorizeTheCapturedPrompt()
    {
        await using var f = new Fixture();
        await f.Begin();
        f.Dispatcher.UseForeignContext = true;
        f.Capture.Transcript("private captured prompt");
        await f.Controller.Quiescence;
        f.Applied.Should().Be(0);
        f.Controller.Status.Should().Contain("rejected");
        f.Logger.Messages.Should().NotContain(message => message.Contains("private captured prompt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Topic")]
    [InlineData("topic two")]
    [InlineData("topic-")]
    [InlineData(" topic")]
    [InlineData("")]
    public async Task NamesNeverUseFuzzyMatchingAliasesOrAnotherDeclaration(string name)
    {
        await using var f = new Fixture();
        await f.Controller.Begin(f.Draft, name, () => f.Focused, f.Apply, Token);
        f.Capture.Opens.Should().Be(0);
        f.Applied.Should().Be(0);
        f.Controller.Status.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("host")]
    [InlineData("edit")]
    [InlineData("other-field")]
    [InlineData("source")]
    [InlineData("reload")]
    [InlineData("dispose")]
    [InlineData("revision")]
    [InlineData("privacy")]
    [InlineData("microphone")]
    [InlineData("call")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    [InlineData("generation")]
    public async Task ExactTargetChangesSuppressLateValueWithoutRetargeting(string change)
    {
        await using var f = new Fixture();
        await f.Begin();
        switch (change)
        {
            case "focus": f.Focused = false; break;
            case "host": f.Admission = null; break;
            case "edit": f.Draft.SetValue("topic", "newer"); break;
            case "other-field": f.Draft.SetValue("topic-two", "newer"); break;
            case "source": f.FreshSource = false; break;
            case "reload": f.Catalogue.ReportReloadFailure(new IOException("Synthetic")); break;
            case "dispose": f.Draft.Dispose(); break;
            case "revision": f.Catalogue.Publish([f.Source with { OwnedInstructionRevision = "foreign" }]); break;
            case "privacy": f.Admission = f.Admission! with { TopologyRevision = 2 }; break;
            case "microphone": f.Admission = f.Admission! with { Microphone = new("foreign", "Synthetic") }; break;
            case "call": f.Admission = f.Admission! with { CallRevision = 2 }; break;
            case "expiry": f.Time.Advance(TimeSpan.FromSeconds(75)); break;
            case "cancel": f.Controller.Retire("Cancel"); break;
            case "generation": f.Capture.AdvanceGeneration(); break;
        }
        f.Controller.Revalidate();
        f.Capture.Transcript("late private", f.Generation);
        await f.Controller.Quiescence;
        f.Applied.Should().Be(0);
    }

    [Theory]
    [InlineData("opening")]
    [InlineData("freshness")]
    [InlineData("dispatch")]
    [InlineData("release")]
    public async Task CancellationJoinsOwnedWorkAndRejectsDelayedHandoff(string boundary)
    {
        await using var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task? opening = null;
        if (boundary is "opening") { f.Capture.Opening = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        if (boundary is "freshness") { f.FreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        if (boundary is "opening" or "freshness")
        { opening = f.Controller.Begin(f.Draft, "topic", () => f.Focused, f.Apply, cancellation.Token); }
        else
        {
            await f.Controller.Begin(f.Draft, "topic", () => f.Focused, f.Apply, cancellation.Token);
            f.Generation = f.Capture.Generation;
            if (boundary is "dispatch") { f.Dispatcher.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            else { f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            f.Capture.Transcript("private delayed");
        }
        await cancellation.CancelAsync();
        f.Dispatcher.Hold?.TrySetResult();
        f.Capture.Closing?.TrySetResult();
        if (opening is not null) { await opening; }
        await f.Controller.Quiescence;
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Applied.Should().Be(0);
        f.Capture.Admitted?.Invoke().Should().BeFalse();
    }

    [Theory]
    [InlineData("open")]
    [InlineData("ack")]
    [InlineData("recognition")]
    [InlineData("empty")]
    [InlineData("release")]
    public async Task FailuresNeverPublishOrAutomaticallyReopen(string failure)
    {
        var f = new Fixture();
        try
        {
            if (failure is "open") { f.Capture.OpenFailure = new IOException("Synthetic"); }
            if (failure is "ack") { f.Capture.RejectAcknowledgement = true; }
            await f.Begin();
            if (failure is "recognition") { f.Capture.Fail(); }
            if (failure is "empty") { f.Capture.Complete(); }
            if (failure is "release")
            {
                f.Capture.StopFailure = new IOException("Synthetic");
                f.Capture.Transcript("must not publish");
                await ((Func<Task>)(() => f.Controller.Quiescence)).Should().ThrowAsync<IOException>();
                await f.Begin();
                f.Capture.Opens.Should().Be(1);
            }
            else { await f.Controller.Quiescence; }
            f.Applied.Should().Be(0);
        }
        finally
        {
            if (failure is "release") { await ((Func<Task>)(async () => await f.DisposeAsync())).Should().ThrowAsync<IOException>(); }
            else { await f.DisposeAsync(); }
        }
    }

    [Theory]
    [InlineData("length")]
    [InlineData("nul")]
    [InlineData("unicode")]
    public async Task InvalidRecognizedValuesAreRejectedWithoutTruncationOrRepair(string invalid)
    {
        await using var f = new Fixture();
        await f.Begin();
        f.Capture.Transcript(invalid switch { "length" => new string('a', 513), "nul" => "a\0b", _ => "\ud800" });
        await f.Controller.Quiescence;
        f.Applied.Should().Be(0);
        f.Controller.Status.Should().Contain("rejected");
    }

    [Fact]
    public async Task QueuedOldExpiryCannotRetireANewerExplicitInputTarget()
    {
        await using var f = new Fixture();
        await f.Begin();
        var retiredExpiry = f.Time.Timers.Single();
        f.Controller.Retire("Cancel exact first target");
        await f.Controller.Quiescence;
        await f.Begin();
        retiredExpiry.Tick();
        f.Controller.IsActive.Should().BeTrue();
        f.Capture.IsListening.Should().BeTrue();
        f.Capture.Transcript("new exact target");
        await f.Controller.Quiescence;
        f.Applied.Should().Be(1);
    }

    [Fact]
    public async Task QuiescenceJoinsDeferredFreshnessPublicationAndItsFinalNativeRelease()
    {
        await using var f = new Fixture();
        await f.Begin();
        f.FreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Transcript("private");
        var pending = f.Controller.Quiescence;
        pending.IsCompleted.Should().BeFalse();
        f.Applied.Should().Be(0);
        f.FreshGate.SetResult(true);
        await pending;
        f.Applied.Should().Be(1);
        f.Controller.IsActive.Should().BeFalse();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExpansionBudgetAndUnicodeRemainOwnedByExistingParser()
    {
        await using var f = new Fixture(string.Concat(Enumerable.Repeat("${input:topic}", 9)));
        await f.Begin();
        var value = string.Concat(Enumerable.Repeat("e\u0301\U0001f642", 128));
        f.Capture.Transcript(value);
        await f.Controller.Quiescence;
        f.Applied.Should().Be(1);
        await ((Func<Task>)(() => f.Draft.Preview(f.Fresh, Token))).Should().ThrowAsync<InvalidDataException>();
        f.Draft.CanUse.Should().BeFalse();
    }

    [Fact]
    public async Task CausalTracesKeepPrivateTextAndNamesOutOfTagsAndUseVoiceOrigin()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        await using var f = new Fixture();
        using var hostile = new Activity("untrusted").SetIdFormat(ActivityIdFormat.W3C).Start();
        await f.Begin();
        f.Capture.Transcript("private literal marker");
        await f.Controller.Quiescence;
        stopped.Should().NotBeEmpty();
        stopped.Should().Contain(activity => activity.Links.Any());
        stopped.SelectMany(activity => activity.TagObjects).Select(tag => tag.Value)
            .Should().NotContain("private literal marker").And.NotContain("topic");
        stopped.Should().OnlyContain(activity => activity.Status == ActivityStatusCode.Ok);
        stopped.First().ParentId.Should().BeNull();
        stopped.Should().OnlyContain(activity => Equals(activity.GetTagItem("kora.origin"), "ActivatedVoice"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired-open")]
    [InlineData("expired-delivery")]
    public async Task MissingOrExpiredHostTraceCannotOpenOrPublishLiteralInput(string trace)
    {
        await using var f = new Fixture();
        if (trace is "missing") { f.StopTracing(); }
        if (trace is "expired-open") { f.BeforeFresh = () => Activity.Current!.Stop(); }
        await f.Begin();
        if (trace is "expired-delivery")
        {
            f.BeforeFresh = () => Activity.Current!.Stop();
            f.Capture.Transcript("private");
        }
        await f.Controller.Quiescence;
        f.Applied.Should().Be(0);
        f.Capture.Opens.Should().Be(trace is "expired-delivery" ? 1 : 0);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        public QuestionReplyFixture.ReplyCapture Capture { get; } = new();
        public PromptDispatcher Dispatcher { get; } = new();
        public EnabledLogger<PromptInputVoiceController> Logger { get; } = new();
        public QuestionReplyFixture.ReplyTime Time { get; } = new();
        public QuestionVoiceAdmission? Admission { get; set; } = new(new("mic", "Synthetic"), 1, 1, 1);
        public ArtifactDefinition Source { get; }
        public ArtifactCatalogue Catalogue { get; }
        public PromptParameterDraft Draft { get; }
        public PromptInputVoiceController Controller { get; }
        public bool Focused { get; set; } = true;
        public bool FreshSource { get; set; } = true;
        public int Applied { get; private set; }
        public long Generation { get; set; }
        public TaskCompletionSource<bool>? FreshGate { get; set; }
        public Action? BeforeFresh { get; set; }
        public Fixture(string body = "Request ${input:topic} / ${input:topic-two}", TimeProvider? time = null)
        {
            ActivitySource.AddActivityListener(listener);
            Source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 2\nname: example\ndescription: Test\n---\n" + body, false),
                ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
            Catalogue = new([Source]);
            Draft = new(Source, Catalogue, () => true);
            Controller = new(Capture, Dispatcher, () => Admission, Fresh, Logger, time ?? Time);
        }
        public Task<bool> Fresh(ArtifactDefinition source, CancellationToken token)
        {
            BeforeFresh?.Invoke();
            return FreshGate is null ? Task.FromResult(FreshSource && ReferenceEquals(source, Source)) : FreshGate.Task.WaitAsync(token);
        }
        public void StopTracing() => listener.Dispose();
        public void Apply(string name, string text) { Draft.SetValue(name, text); Applied++; }
        public async Task Begin()
        {
            await Controller.Begin(Draft, "topic", () => Focused, Apply, Token);
            Generation = Capture.Generation;
        }
        public async ValueTask DisposeAsync()
        {
            try { await Controller.DisposeAsync(); }
            finally { Draft.Dispose(); listener.Dispose(); }
        }
    }

    private sealed class PromptDispatcher : IUiDispatcher
    {
        public TaskCompletionSource? Hold { get; set; }
        public bool UseForeignContext { get; set; }
        public async Task InvokeAsync(Func<Task> action)
        {
            if (Hold is not null) { await Hold.Task; }
            using var foreign = UseForeignContext
                ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Presentation)
                : null;
            await action();
        }
        public void Post(Action action) => action();
    }

    private sealed class UnavailableExpiryTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new IOException("Synthetic expiry timer unavailable");
    }
}
