using System.Diagnostics;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Application.Diagnostics.EvidenceAnswers;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class EvidenceAnswerWindowTests
{
    [Fact]
    public async Task NativeSearchReviewSeparateConfirmationFullContextCitationsAndOneShotResult()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var window = new EvidenceWindow(f.Model);
            window.Show();
            try
            {
                await f.Model.SearchAsync();
                f.Reads.Should().Be(1);
                window.FindControl<Expander>("AskEvidence")!.IsExpanded = true;
                window.UpdateLayout();
                var question = window.GetVisualDescendants().OfType<TextBox>().Single(control =>
                    string.Equals(AutomationProperties.GetName(control), "Original question about this exact diagnostic page", StringComparison.Ordinal));
                question.Text = "What was recorded?";
                f.Model.AnswerQuestion.Should().Be(question.Text);
                f.Model.CanConfirmEvidenceAnswer.Should().BeFalse();
                f.Calls.Should().Be(0);
                FindButton(window, "Review complete page and question").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                f.Model.CanConfirmEvidenceAnswer.Should().BeTrue();
                f.Model.AnswerReviewText.Should().Contain(f.Model.ResultText).And.Contain("What was recorded?")
                    .And.Contain(EvidenceAnswerResponse.Policy).And.Contain("NEW Local context conversation");
                f.PolicyCalls.Should().Be(0);
                f.Calls.Should().Be(0);
                var records = window.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                    string.Equals(AutomationProperties.GetName(control), "Exact stable record citations in the reviewed page", StringComparison.Ordinal));
                records.SelectedItem = f.Record.Reference.Citation;
                FindButton(window, "Show exact inert record (no source read)").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                f.Model.AnswerCitationText.Should().Contain("private native diagnostic");
                var confirm = FindButton(window, "Confirm one tool-free local answer in a new conversation");
                await ((AsyncCommand)confirm.Command!).ExecuteAsync();
                f.Model.AnswerStatus.Should().Contain("Succeeded").And.Contain("Actual durable");
                f.Model.AnswerText.Should().Be("Validated cited answer.");
                f.Model.CanConfirmEvidenceAnswer.Should().BeFalse();
                await f.Model.ConfirmEvidenceAnswerAsync();
                f.Calls.Should().Be(1);
                f.Reads.Should().Be(1, "review, answer and inert citation navigation must never reread evidence");
                f.Model.ShowEvidenceAnswerCitation(f.Record.Reference.Citation);
                f.Model.AnswerCitationText.Should().Contain("private native diagnostic");
                f.Model.AnswerQuestion = "Changed original";
                f.Model.AnswerText.Should().BeEmpty();
                f.Model.AnswerReviewText.Should().BeEmpty();
                f.Model.AnswerCitationText.Should().BeEmpty();
            }
            finally { window.Close(); }
            f.Model.AnswerQuestion.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData("question")]
    [InlineData("filter")]
    [InlineData("source")]
    [InlineData("page")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("deadline")]
    public async Task ReviewAndInFlightRevocationSuppressLateOutputAndWaitForIgnoringDependency(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var window = new EvidenceWindow(f.Model);
            window.Show();
            try
            {
                await f.Model.SearchAsync();
                f.Model.AnswerQuestion = "What happened?";
                f.Model.ReviewEvidenceAnswer();
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var join = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                f.Hold = () => release.Task;
                f.Join = () => join.Task;
                var operation = f.Model.ConfirmEvidenceAnswerAsync();
                f.Calls.Should().Be(1);
                switch (change)
                {
                    case "question": f.Model.AnswerQuestion = "Changed"; break;
                    case "filter": f.Model.SafeText = "changed"; break;
                    case "source": f.Model.Source = EvidenceSource.Audit; break;
                    case "page": await f.Model.SearchAsync(); break;
                    case "owner": f.Host = false; break;
                    case "privacy": f.CanInspect = false; break;
                    case "control": f.ControlRevision++; break;
                    case "deadline": f.Time.Now = f.Time.Now.AddMinutes(3); break;
                    case "cancel": f.Model.CancelEvidenceAnswer(); break;
                    case "close": window.Close(); break;
                }

                f.Model.RefreshEvidenceAnswerAdmission();
                f.Token.IsCancellationRequested.Should().BeTrue();
                operation.IsCompleted.Should().BeFalse();
                f.Model.IsAnswering.Should().BeTrue();
                release.SetResult();
                await f.JoinEntered.Task;
                operation.IsCompleted.Should().BeFalse();
                join.SetResult();
                await operation;
                f.Model.IsAnswering.Should().BeFalse();
                f.Model.AnswerText.Should().BeEmpty();
                f.Model.AnswerReviewText.Should().BeEmpty();
                f.Model.AnswerCitations.Should().BeEmpty();
                f.Model.AnswerCitationText.Should().BeEmpty();
                f.Calls.Should().Be(1);
                if (change is "privacy" or "close") { f.Model.AnswerQuestion.Should().BeEmpty(); }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("empty", "NoEvidence")]
    [InlineData("partial", "Unavailable")]
    [InlineData("omitted", "Unavailable")]
    [InlineData("invalid-question", "Unavailable")]
    [InlineData("source", "Unavailable")]
    public async Task InvalidOrUnsupportedReviewDoesNotCallSourcePolicyOrModel(string mode, string expected)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture { Mode = mode };
            await f.Model.SearchAsync();
            f.Model.AnswerQuestion = mode is "invalid-question" ? new string('x', 257) : "What happened?";
            if (mode is "source") { f.Model.Source = EvidenceSource.Audit; }
            f.Model.ReviewEvidenceAnswer();
            f.Model.AnswerStatus.Should().Contain(expected);
            await f.Model.ConfirmEvidenceAnswerAsync();
            f.PolicyCalls.Should().Be(0);
            f.Calls.Should().Be(0);
            f.Model.Close();
        });
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("control")]
    [InlineData("privacy")]
    [InlineData("source")]
    [InlineData("filter")]
    [InlineData("question")]
    [InlineData("cancel")]
    [InlineData("close")]
    public async Task ChangedReviewCannotStartAnyModelOrDurableConversation(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Model.SearchAsync();
            f.Model.AnswerQuestion = "Question?";
            f.Model.ReviewEvidenceAnswer();
            switch (change)
            {
                case "owner": f.Host = false; break;
                case "control": f.ControlRevision++; break;
                case "privacy": f.CanInspect = false; break;
                case "source": f.Model.Source = EvidenceSource.Audit; break;
                case "filter": f.Model.SafeText = "changed"; break;
                case "question": f.Model.AnswerQuestion = "changed"; break;
                case "cancel": f.Model.CancelEvidenceAnswer(); break;
                case "close": f.Model.Close(); break;
            }
            await f.Model.ConfirmEvidenceAnswerAsync();
            f.Model.CanConfirmEvidenceAnswer.Should().BeFalse();
            f.Model.AnswerReviewText.Should().BeEmpty();
            f.PolicyCalls.Should().Be(0);
            f.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task AmbientNonOriginalOriginCannotBeRewrittenIntoLocalConfirmation(RequestOrigin origin)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Model.SearchAsync();
            f.Model.AnswerQuestion = "Question?";
            f.Model.ReviewEvidenceAnswer();
            using var scope = HostActivity.BeginRoot(HostRequest.Create(origin),
                HostActivityLayer.Application, HostOperation.Request);
            await f.Model.ConfirmEvidenceAnswerAsync();
            f.Model.AnswerStatus.Should().Contain("Denied");
            f.Calls.Should().Be(0);
            f.Model.ReviewEvidenceAnswer();
            f.Model.AnswerStatus.Should().Contain("Denied");
            f.Model.AnswerReviewText.Should().BeEmpty();
        });
    }

    private static Button FindButton(Window window, string content) => window.GetVisualDescendants().OfType<Button>()
        .Single(control => string.Equals(control.Content as string, content, StringComparison.Ordinal));

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable, IEvidenceReader, IEvidenceQueryAccess, ISessionWorkspaceAccess,
        IEvidenceAnswerSourceUsePolicy, IEvidenceAnswerConversation, ISecurityAuditLog
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        internal Fixture()
        {
            ActivitySource.AddActivityListener(listener);
            Record = new(new(EvidenceSource.Log, new(Guid.NewGuid())), Time.Now, Time.Now.AddHours(1),
                EvidenceSegmentStatus.Present, HostRequest.Create(RequestOrigin.HostSystem), null, null, null,
                "Information", "Example", 42, "private native diagnostic", new Dictionary<string, EvidenceValue>(StringComparer.Ordinal),
                null, null, []);
            Model = new(new DurableEvidenceQuery(this, this, Time, NullLogger<DurableEvidenceQuery>.Instance),
                () => CanInspect, NullLogger<EvidenceViewModel>.Instance) { Source = EvidenceSource.Log };
            Model.BindEvidenceAnswers(new(this, this, this, this, Time, NullLogger<EvidenceAnswerWorkflow>.Instance),
                () => () => Host, () => ControlRevision, Time);
        }
        internal Clock Time { get; } = new();
        internal EvidenceRecord Record { get; }
        internal EvidenceViewModel Model { get; }
        internal string? Mode { get; init; }
        internal bool Host { get; set; } = true;
        public bool CanInspect { get; set; } = true;
        public bool CanControl => Host;
        public long ControlRevision { get; set; }
        internal int Reads { get; private set; }
        internal int Calls { get; private set; }
        internal int PolicyCalls { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal Func<Task>? Hold { get; set; }
        internal Func<Task>? Join { get; set; }
        internal TaskCompletionSource JoinEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Reads++;
            var record = Mode is "omitted" ? Record with { ContentOmitted = true } : Record;
            return ValueTask.FromResult(new EvidenceReadBatch(new(1, 0, 0, 0), Mode is "empty" ? []
                : [new(new(0, EvidenceSource.Log, Record.Reference.Id.Value.ToString("D"), 0), record)],
                null, false, false, Mode is "partial" ? EvidencePageStatus.Partial : null));
        }
        public Task<EvidenceSourceUseDecision> AdmitAsync(EvidenceAnswerReview review, CancellationToken token)
        {
            PolicyCalls++;
            return Task.FromResult(EvidenceSourceUseDecision.Allowed);
        }
        public async Task<ConversationSubmissionReceipt> SubmitAsync(EvidenceAnswerReview review, Func<bool> current,
            Func<CancellationToken, Task> validate, CancellationToken token)
        {
            Calls++;
            Token = token;
            review.Page.Should().Be(Model.ResultText);
            review.Records.Keys.Should().Equal(Record.Reference.Citation);
            await validate(token);
            if (Hold is not null) { await Hold(); }
            var request = new HostRequest(new(Guid.NewGuid()), review.Destination.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
            return new(new(request, new(1), 1, 3, Time.Now, review.Question, ModelTurnOutcome.Succeeded, ModelTurnReason.None)
            {
                Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
                AssistantResponse = new(provenance, "Validated cited answer."),
            }, new(request, new(3), HostTaskState.Succeeded));
        }
        public async Task JoinAsync()
        {
            JoinEntered.TrySetResult();
            if (Join is not null) { await Join(); }
        }
        public void Write(SecurityAuditEvent auditEvent) { }
        public void Dispose() { Model.Close(); listener.Dispose(); }
    }
}
