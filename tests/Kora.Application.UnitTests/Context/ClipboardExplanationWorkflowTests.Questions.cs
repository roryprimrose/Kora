using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Context;

public sealed partial class ClipboardExplanationWorkflowTests
{
    [Theory]
    [InlineData("once", 2)]
    [InlineData("session", 1)]
    [InlineData("perpetual", 1)]
    public async Task SameCaptureQuestionIsExactSeparateOneUseWithActualPermissionAndNoAnswerHistory(string scope, int grants)
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        fixture.GrantChoice = scope;
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var source = fixture.Broker.Current!;
        await fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token);
        fixture.Time.Now += TimeSpan.FromSeconds(10);
        const string question = "  Which exact value is mentioned?\r\nKeep the original spaces.\t";
        var review = fixture.Workflow.ReviewQuestion(source.SnapshotId, question, () => true);
        review.Question.Should().Be(question);
        review.Source.Should().BeSameAs(source);
        review.Request.Should().Be(root.Request);
        fixture.Adapter.Calls.Should().Be(1, "review is not consent or inference");
        (await fixture.Workflow.AnswerAsync(review, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Succeeded);
        var context = ModelToolProtocol.ReadContext(fixture.Adapter.Wire!, fixture.Adapter.Provenance!);
        context.UserRequest.Should().Be(question);
        context.ToolMode.Should().Be(ModelToolMode.None);
        context.Evidence.Should().ContainSingle().Which.Content.Should().Be(source.Text);
        context.Evidence[0].Id.Value.Should().Be(source.SnapshotId);
        context.Evidence[0].Revision.Value.Should().Be(source.Version);
        context.Evidence[0].Disclosure.Should().Be(ModelEvidenceDisclosure.LocalOnly);
        System.Text.Encoding.UTF8.GetString(fixture.Adapter.Wire!).Should().NotContain("Genuine synthetic local explanation.");
        fixture.Workflow.CurrentExplanation.Should().Be("Genuine synthetic local explanation.");
        (await fixture.Workflow.AnswerAsync(review, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Denied);
        fixture.Adapter.Calls.Should().Be(2);
        fixture.Reader.Calls.Should().Be(1);
        fixture.Broker.Current.Should().BeSameAs(source);
        fixture.Broker.Current!.CapturedAt.Should().Be(DateTimeOffset.UnixEpoch);
        fixture.PermissionQuestions.Should().Be(grants);
        fixture.Messages.Should().NotContain(message => message.Contains(question, StringComparison.Ordinal));
        fixture.Audits.Should().NotContain(item => item.TargetId.Contains(question, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("space")]
    [InlineData("unicode")]
    [InlineData("control")]
    [InlineData("long")]
    [InlineData("credential")]
    public async Task InvalidOriginalQuestionIsRejectedWholeBeforePermissionOrModel(string fault)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var question = fault switch
        {
            "empty" => "",
            "space" => " \t\r\n",
            "unicode" => "\ud800",
            "control" => "a\0b",
            "long" => new string('x', 4097),
            _ => "-----BEGIN PRIVATE KEY-----\nsynthetic secret\n-----END PRIVATE KEY-----",
        };
        var prepare = () => fixture.Workflow.ReviewQuestion(fixture.Broker.Current!.SnapshotId, question, () => true);
        if (fault is "credential") { prepare.Should().Throw<Kora.Core.Conversation.ConversationCredentialException>(); }
        else { prepare.Should().Throw<InvalidDataException>(); }
        fixture.Adapter.Calls.Should().Be(0);
        fixture.PermissionQuestions.Should().Be(0);
        fixture.Reader.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-id")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("ineligible")]
    [InlineData("disposed")]
    public async Task UnavailableSourceOrInputCannotPrepareOrRecapture(string fault)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var id = fixture.Broker.Current!.SnapshotId;
        switch (fault)
        {
            case "missing": fixture.Broker.Clear(); break;
            case "wrong-id": id = Guid.NewGuid(); break;
            case "expired": fixture.Time.Now += ClipboardExplanationContext.Lifetime; break;
            case "future": fixture.Time.Now -= TimeSpan.FromSeconds(1); break;
            case "disposed": fixture.Workflow.Dispose(); break;
        }
        var prepare = () => fixture.Workflow.ReviewQuestion(id, "What does this mean?", () => fault is not "ineligible");
        prepare.Should().Throw<InvalidOperationException>();
        fixture.Reader.Calls.Should().Be(1);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    public async Task NonOriginalNativeInputCannotSupplyQuestionAuthority(RequestOrigin origin, bool invocation)
    {
        await using var fixture = new Fixture();
        using var capture = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var request = HostRequest.Create(origin);
        if (invocation) { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var input = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var prepare = () => fixture.Workflow.ReviewQuestion(fixture.Broker.Current!.SnapshotId, "Question", () => true);
        prepare.Should().Throw<InvalidOperationException>();
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("retired")]
    [InlineData("clear")]
    [InlineData("new-review")]
    [InlineData("source")]
    [InlineData("expiry")]
    [InlineData("eligibility")]
    [InlineData("disposed")]
    [InlineData("wrong-request")]
    public async Task RetiredOrWrongOriginalQuestionCannotReviveOrDispatch(string fault)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var eligible = true;
        var review = fixture.Workflow.ReviewQuestion(fixture.Broker.Current!.SnapshotId, "Question", () => eligible);
        switch (fault)
        {
            case "retired": review.Retire(); break;
            case "clear": fixture.Workflow.Clear(); break;
            case "new-review": fixture.Workflow.ReviewQuestion(review.Source.SnapshotId, "Another question", () => true); break;
            case "source": fixture.Broker.Clear(); break;
            case "expiry": fixture.Time.Now += ClipboardExplanationContext.Lifetime; break;
            case "eligibility": eligible = false; break;
            case "disposed": fixture.Workflow.Dispose(); break;
        }
        using var other = fault is "wrong-request" ? fixture.Root() : null;
        (await fixture.Workflow.AnswerAsync(review, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Denied);
        if (fault is not "wrong-request") { review.IsCurrent().Should().BeFalse(); }
        eligible = true;
        fixture.Time.Now = DateTimeOffset.UnixEpoch;
        if (fault is not "wrong-request") { review.IsCurrent().Should().BeFalse("retirement is permanent"); }
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Reader.Calls.Should().Be(1);
        var missing = () => fixture.Workflow.AnswerAsync(null!, fixture.Token);
        await missing.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("unqualified", ClipboardExplanationOutcome.Unavailable)]
    [InlineData("denied", ClipboardExplanationOutcome.SourceUseDenied)]
    [InlineData("oversize", ClipboardExplanationOutcome.EnvelopeRejected)]
    public async Task QuestionCannotBypassQualificationPermissionOrCompleteEnvelopeBudget(string fault, ClipboardExplanationOutcome expected)
    {
        await using var fixture = new Fixture(qualified: fault is not "unqualified", productionSourcePolicy: true);
        using var root = fixture.Root();
        if (fault is "oversize") { fixture.Reader.Text = new string('x', 32768); }
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        if (fault is "denied") { fixture.GrantChoice = null; }
        var review = fixture.Workflow.ReviewQuestion(fixture.Broker.Current!.SnapshotId, "Question", () => true);
        (await fixture.Workflow.AnswerAsync(review, fixture.Token)).Outcome.Should().Be(expected);
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("retire")]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("expiry")]
    public async Task QuestionRetirementSuppressesLateAnswerAndJoinsTheActualAdapter(string fault)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var eligible = true;
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var review = fixture.Workflow.ReviewQuestion(fixture.Broker.Current!.SnapshotId, "Question", () => eligible);
        fixture.Adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = fixture.Workflow.AnswerAsync(review, cancellation.Token);
        await fixture.Adapter.Started.Task.WaitAsync(fixture.Token);
        switch (fault)
        {
            case "cancel": cancellation.Cancel(); break;
            case "retire": review.Retire(); cancellation.Cancel(); break;
            case "source": fixture.Broker.Clear(); break;
            case "owner": eligible = false; break;
            case "expiry": fixture.Time.Now += ClipboardExplanationContext.Lifetime; break;
        }
        var join = fixture.Workflow.WaitForQuiescenceAsync();
        join.IsCompleted.Should().BeFalse();
        fixture.Adapter.Pending.SetResult(fixture.Adapter.Reply());
        (await run).Outcome.Should().NotBe(ClipboardExplanationOutcome.Succeeded);
        await join;
        fixture.Workflow.IsQuiescent.Should().BeTrue();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        fixture.Reader.Calls.Should().Be(1);
        fixture.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SourceRevokedDuringTerminalCommitCannotPublishTheQuestionAnswer()
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var review = fixture.Workflow.ReviewQuestion(fixture.Broker.Current!.SnapshotId, "Question", () => true);
        fixture.BeforeTaskCommit = task =>
        {
            if (task.State == HostTaskState.Succeeded && fixture.Adapter.Calls != 0) { fixture.Broker.Clear(); }
        };
        (await fixture.Workflow.AnswerAsync(review, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Cancelled);
        fixture.Adapter.Calls.Should().Be(1);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Fact]
    public async Task GrantRevokedBeforeQuestionResponseBoundaryCannotPublish()
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var review = fixture.Workflow.ReviewQuestion(fixture.Broker.Current!.SnapshotId, "Question", () => true);
        fixture.Adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = fixture.Workflow.AnswerAsync(review, fixture.Token);
        await fixture.Adapter.Started.Task.WaitAsync(fixture.Token);
        RevokePermission(fixture);
        fixture.Adapter.Pending.SetResult(fixture.Adapter.Reply());
        var operation = () => run;
        await operation.Should().ThrowAsync<InvalidOperationException>();
        await fixture.Workflow.WaitForQuiescenceAsync();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(1);
    }
}
