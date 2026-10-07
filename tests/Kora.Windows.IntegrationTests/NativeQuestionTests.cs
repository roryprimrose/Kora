using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class NativeQuestionTests
{
    [Theory]
    [InlineData(QuestionKind.SingleChoice)]
    [InlineData(QuestionKind.MultipleChoice)]
    [InlineData(QuestionKind.Text)]
    public async Task Native_drafts_and_explicit_submit_use_real_services_store_and_original_target(QuestionKind kind)
    {
        using var f = new InteractionStorageFixture();
        f.Request = InteractionStorageFixture.NewRequest(origin: RequestOrigin.ActivatedVoice);
        await f.InitializeAsync();
        var record = await QuestionAsync(f, kind);
        var model = Model(f, record);
        var changes = new List<string?>();
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        await model.RefreshTargetAsync();
        model.ReviewText.Should().BeEmpty();
        model.CanSubmit.Should().BeFalse();
        model.Answer.Choices.Should().BeEmpty();
        model.Edit(kind == QuestionKind.Text ? new([], "local answer")
            : kind == QuestionKind.MultipleChoice ? new(["a", "b"]) : new(["a"]));
        model.CanSubmit.Should().BeTrue();
        await model.SaveDraftAsync();
        model.Key.Revision.Value.Should().Be(2);
        model.Completion.IsCompleted.Should().BeFalse();
        model.Answer.Text.Should().Be(kind == QuestionKind.Text ? "local answer" : null);
        await model.ReviewAsync();
        model.ReviewText.Should().Contain(record.Key.QuestionId.Value.ToString("D"));
        using var review = JsonDocument.Parse(model.ReviewText);
        review.RootElement.GetProperty("Key").GetProperty("Revision").GetProperty("Value").GetInt64().Should().Be(2);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        await model.SubmitAsync();
        var result = await model.Completion;
        result.Outcome.Should().Be(HostInteractionOutcome.Answered);
        result.Question!.Key.Request.Should().Be(f.Request);
        result.Question.AnswerChannel.Should().Be(RequestOrigin.LocalUi);
        result.Question.Key.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        model.IsEditable.Should().BeFalse();
        model.CanSubmit.Should().BeFalse();
        changes.Should().Contain(nameof(NativeQuestionViewModel.CanSubmit));
        f.Reopen();
        var persisted = (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single();
        persisted.Key.Should().Be(result.Question.Key);
        persisted.Status.Should().Be(QuestionStatus.Answered);
        persisted.Draft.Should().BeEquivalentTo(result.Question.Draft);
    }

    [WindowsFact]
    public async Task Invalid_text_is_visible_not_truncated_and_is_denied_by_service_even_if_invoked_directly()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var model = Model(f, await QuestionAsync(f, QuestionKind.Text));
        model.Edit(new([], new string('x', 33)));
        model.Answer.Text.Should().HaveLength(33);
        model.CanSubmit.Should().BeFalse();
        model.CanSaveDraft.Should().BeFalse();
        model.Status.Should().Contain("Invalid answer");
        await model.SubmitAsync();
        (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
    }

    [Theory]
    [InlineData("revise")]
    [InlineData("cancel")]
    [InlineData("generation")]
    [InlineData("foreign")]
    public async Task Stale_closed_revised_foreign_or_resumed_targets_do_not_retarget_native_input(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var record = await QuestionAsync(f);
        var model = Model(f, string.Equals(change, "foreign", StringComparison.Ordinal)
            ? record with { Key = new(f.Request, new(Guid.NewGuid()), new(1)) } : record);
        var originalKey = model.Key;
        model.Edit(new(["a"]));
        if (string.Equals(change, "revise", StringComparison.Ordinal))
        {
            await f.RunAsync(() => f.Questions.ReviseAsync(record.Key, record.Spec, f.Time.Now.AddMinutes(5), f.Token));
        }
        if (string.Equals(change, "cancel", StringComparison.Ordinal)) { await f.RunAsync(() => f.Questions.CancelAsync(record.Key, f.Token)); }
        if (string.Equals(change, "generation", StringComparison.Ordinal))
        {
            await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(1), active: false, remove: false, f.Token));
            await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(2), active: true, remove: false, f.Token));
            await f.PublishAsyncAfterLifecycle();
        }
        await model.RefreshTargetAsync();
        (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        model.Key.Should().Be(originalKey);
        model.IsEditable.Should().BeFalse();
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expiry_and_unknown_privacy_disable_native_input_and_clear_review_and_edits(bool expire)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var gate = true;
        var record = await QuestionAsync(f);
        var model = Model(f, record, () => gate);
        model.Edit(new(["a"]));
        await model.ReviewAsync();
        if (expire) { f.Time.Now = record.ExpiresAt; }
        else { gate = false; }
        model.RefreshEligibility();
        model.Edit(new(["a"]));
        model.IsEditable.Should().BeFalse();
        model.ReviewText.Should().BeEmpty();
        model.Answer.Choices.Should().BeEmpty();
        await model.SubmitAsync();
        (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
    }

    [WindowsFact]
    public async Task Serialized_live_gate_denies_a_privacy_or_ownership_race_after_button_admission()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var calls = 0;
        var record = await QuestionAsync(f);
        var model = Model(f, record, () => Interlocked.Increment(ref calls) == 1);
        await model.CancelAsync();
        (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
    }

    [WindowsFact]
    public async Task Changing_foreground_host_session_does_not_move_the_original_native_answer()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var record = await QuestionAsync(f);
        var model = Model(f, record);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        model.Edit(new(["a"]));
        await model.SubmitAsync();
        var result = await model.Completion;
        result.Outcome.Should().Be(HostInteractionOutcome.Answered);
        result.Question!.Key.Request.Should().Be(record.Key.Request).And.NotBe(f.Request);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Two_native_presentations_of_one_target_cannot_both_answer_a_raced_revision()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var record = await QuestionAsync(f);
        var first = Model(f, record);
        var second = Model(f, record);
        first.Edit(new(["a"]));
        second.Edit(new(["b"]));
        await Task.WhenAll(first.SubmitAsync(), second.SubmitAsync());
        var outcomes = new[] { (await first.Completion).Outcome, (await second.Completion).Outcome };
        outcomes.Should().ContainSingle(o => o == HostInteractionOutcome.Answered);
        outcomes.Should().ContainSingle(o => o == HostInteractionOutcome.Conflict);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Key.Revision.Value.Should().Be(2);
    }

    [WindowsFact]
    public async Task Approval_requires_exact_immutable_review_and_authorization_never_ordinary_submit_or_use()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await PublishRealFixtureProposalAsync(f);
        var record = await f.PresentAsync();
        var model = Model(f, record);
        model.IsApproval.Should().BeTrue();
        model.Edit(new(["once"]));
        model.CanSubmit.Should().BeFalse();
        await model.SubmitAsync();
        model.Completion.IsCompleted.Should().BeFalse();
        await model.ReviewAsync();
        model.CanSubmit.Should().BeFalse();
        model.CompleteReviewPresentation("not the original review");
        model.CanSubmit.Should().BeFalse();
        model.CompleteReviewPresentation(new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance)
            .Render(model.ReviewText, Kora.Core.Presentation.DetailContentKind.PlainText).SemanticText);
        model.CanSubmit.Should().BeTrue();
        model.ReviewText.Should().Contain(f.Proposal.Binding.ImplementationDigest);
        model.ReviewText.Should().Contain("InvocationDigest").And.Contain("DestinationDigest")
            .And.Contain("TransformationDigest").And.Contain("DeclaredResourceDigest");
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        await model.SaveDraftAsync();
        model.ReviewText.Should().BeEmpty();
        model.CanSubmit.Should().BeFalse();
        await model.ReviewAsync();
        model.CompleteReviewPresentation(model.ReviewText);
        await model.SubmitAsync();
        (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Approved);
        var grant = (await f.Store.ReadGrantsAsync(f.Token)).Single();
        grant.ApprovedProposal.Should().Be(f.Proposal);
        grant.CreatorChannel.Should().Be(RequestOrigin.LocalUi);
        grant.UseCount.Should().Be(0);
        grant.Status.Should().Be(OperationGrantStatus.Active);
        (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!.State.Should().Be(HostTaskState.IntentRecorded);
    }

    [Theory]
    [InlineData("proposal")]
    [InlineData("protected-call")]
    [InlineData("mandatory")]
    public async Task UI_confirmation_cannot_launder_changed_proposals_protected_voice_origin_or_missing_gates(string change)
    {
        using var f = new InteractionStorageFixture();
        f.Request = InteractionStorageFixture.NewRequest(origin: RequestOrigin.ActivatedVoice);
        await f.InitializeAsync();
        await PublishRealFixtureProposalAsync(f, HostOperationEffect.VoiceOrCallSettings);
        var model = Model(f, await f.PresentAsync());
        model.Edit(new(["once"]));
        await model.ReviewAsync();
        model.CompleteReviewPresentation(model.ReviewText);
        var immutable = model.ReviewText;
        if (string.Equals(change, "proposal", StringComparison.Ordinal))
        {
            f.Proposal = new(f.Request, f.Proposal.ProposalId, new(2), f.Proposal.Binding,
                f.Proposal.Effect, f.Proposal.ExpiresAt);
        }
        if (string.Equals(change, "protected-call", StringComparison.Ordinal)) { f.Policy = f.Policy with { IsProtectedCall = true }; }
        if (string.Equals(change, "mandatory", StringComparison.Ordinal)) { f.Policy = f.Policy with { OtherMandatoryGatesSatisfied = false }; }
        await f.PublishAsync();
        model.ReviewText.Should().Be(immutable);
        await model.SubmitAsync();
        (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Real_atomic_audit_failure_rolls_back_answer_and_disables_retries()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var record = await QuestionAsync(f);
        var failure = new FailAudit();
        f.Reopen(failure);
        await f.PublishAsync();
        failure.Enabled = true;
        var model = Model(f, record);
        model.Edit(new(["a"]));
        await model.SubmitAsync();
        var wait = () => model.Completion;
        await wait.Should().ThrowAsync<IOException>();
        model.CanSubmit.Should().BeFalse();
        model.Status.Should().Contain("audit failed");
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Failed_review_audit_does_not_expose_content_or_enable_approval()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var record = await f.PresentAsync();
        var failure = new FailAudit();
        f.Reopen(failure);
        await f.PublishAsync();
        failure.Enabled = true;
        var model = Model(f, record);
        model.Edit(new(["once"]));
        await model.ReviewAsync();
        var wait = () => model.Completion;
        await wait.Should().ThrowAsync<IOException>();
        model.ReviewText.Should().BeEmpty();
        model.CanSubmit.Should().BeFalse();
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_version_question_is_composed_inside_production_durable_query_evidence_and_stores(bool cancel)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var evidence = new WindowsSqliteEvidenceSink(f.Paths);
        evidence.Initialize();
        using var provider = new EvidenceLoggerProvider([evidence], new NoGaps());
        using var logs = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var query = new DurableVersionQuery(new(f.Tasks), new LoggerSecurityAuditLog(logs.CreateLogger<LoggerSecurityAuditLog>()),
            logs.CreateLogger<DurableVersionQuery>());
        var host = new NativeQuestionHost(f.Store, f.Time, logs.CreateLogger<NativeQuestionViewModel>());
        host.BindGate(static () => true);
        HostRequest? original = null;
        var invoked = false;
        var run = async () => await query.RunAsync(RequestOrigin.LocalUi, async () =>
        {
            original = HostActivity.RequireCurrent().Request;
            await host.AskVersionAsync(async model =>
            {
                model.Key.Request.Should().Be(original);
                if (cancel) { await model.CancelAsync(); }
                else
                {
                    await model.ReviewAsync();
                    model.Edit(new(["show"]));
                    await model.SaveDraftAsync();
                    await model.SubmitAsync();
                }
            }, f.Token);
            invoked = true;
        }, f.Token);
        if (cancel)
        {
            await run.Should().ThrowAsync<OperationCanceledException>();
            invoked.Should().BeFalse();
            (await f.Tasks.ReadTaskAsync(original!.TaskId, f.Token))!.State.Should().Be(HostTaskState.DispatchRecorded);
            (await new HostTaskCoordinator(f.Tasks).RecoverAsync(10, f.Token)).Should().Contain(record =>
                record.Request == original && record.State == HostTaskState.Unknown);
        }
        else
        {
            var receipt = await run();
            invoked.Should().BeTrue();
            receipt.Request.Should().Be(original);
            receipt.State.Should().Be(HostTaskState.Succeeded);
            (await f.Tasks.ReadTaskAsync(original!.TaskId, f.Token)).Should().Be(receipt);
        }
        (await f.Store.ReadQuestionsAsync(original!.SessionId, f.Token)).Single().Status.Should()
            .Be(cancel ? QuestionStatus.Cancelled : QuestionStatus.Answered);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        using var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(f.Paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
            Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        database.Open();
        using var count = database.CreateCommand();
        count.CommandText = "SELECT count(*) FROM security_audit_events WHERE request_id=$id;";
        count.Parameters.AddWithValue("$id", original.RequestId.Value.ToString("D"));
        ((long)count.ExecuteScalar()!).Should().Be(cancel ? 1 : 2);
    }

    [Fact]
    public void Native_window_contract_has_bounded_choices_accessible_controls_and_passive_review()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kora.slnx"))) { root = root.Parent; }
        var path = Path.Combine(root!.FullName, "src", "Kora");
        var xaml = File.ReadAllText(Path.Combine(path, "QuestionWindow.axaml"));
        var document = XDocument.Parse(xaml);
        document.Root!.Attribute("Topmost")!.Value.Should().Be("False");
        document.Root.Attribute("WindowStartupLocation")!.Value.Should().Be("CenterOwner");
        foreach (var name in new[] { "AnswerInput", "ExactReview", "ReviewQuestion", "SaveDraft", "SubmitAnswer", "CancelQuestion", "CloseQuestion" })
        {
            var control = document.Descendants().Single(e => e.Attributes().Any(a =>
                string.Equals(a.Name.LocalName, "Name", StringComparison.Ordinal) && string.Equals(a.Value, name, StringComparison.Ordinal)));
            control.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
            control.Attribute("TabIndex").Should().NotBeNull();
            control.Attribute("MaxLength").Should().BeNull();
        }
        var source = File.ReadAllText(Path.Combine(path, "QuestionWindow.axaml.cs"));
        source.Should().Contain("new RadioButton").And.Contain("new CheckBox")
            .And.Contain("DetailContentKind.PlainText").And.Contain("Key.Escape")
            .And.Contain("CopyingToClipboard += BlockClipboard").And.Contain("expiry.Stop()")
            .And.NotContain("NotifyPresenceInteraction").And.NotContain("WebView").And.NotContain("ConsumeAsync");
        xaml.Should().Contain("AutomationProperties.LiveSetting=\"Polite\"").And.NotContain("WebView");
        File.ReadAllText(Path.Combine(path, "App.axaml.cs")).Should().Contain("new QuestionWindowController")
            .And.Contain("questionWindow?.Dispose()");
    }

    private static NativeQuestionViewModel Model(InteractionStorageFixture f, HostQuestionRecord record, Func<bool>? gate = null)
    {
        gate ??= static () => true;
        var guarded = new NativeInteractionStore(f.Store, gate);
        return new(record, new(guarded, f.Time), new(guarded, f.Time), new(guarded, f.Time), gate,
            f.Time, NullLogger<NativeQuestionViewModel>.Instance);
    }

    private static async Task<HostQuestionRecord> QuestionAsync(InteractionStorageFixture f, QuestionKind kind = QuestionKind.SingleChoice)
    {
        var spec = kind == QuestionKind.Text
            ? new QuestionSpec("Bounded local answer", kind, [], maximumTextLength: 32)
            : new("Choose", kind, [new("a", "First"), new("b", "Second")], maximum: kind == QuestionKind.MultipleChoice ? 2 : 1);
        return (await f.RunAsync(() => f.Questions.CreateAsync(f.Request, spec, f.Time.Now.AddMinutes(5), f.Token))).Question!;
    }

    private static async Task PublishRealFixtureProposalAsync(InteractionStorageFixture f, HostOperationEffect effect = HostOperationEffect.BoundedRead)
    {
        // Each digest is actually computed over this disposable fixture's immutable canonical descriptor.
        static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var binding = new ExactOperationBinding("bounded.read", "builtin", "fixture",
            Hash("definition:v1"), Hash("declared:none"), Hash("tracked:fixture"), Hash("implementation:fixture-v1"),
            Hash("invocation:read"), Hash("resources:fixture"), Hash("identity:fixture"), Hash("destination:native"),
            Hash("transform:none"), new(1));
        f.Proposal = new(f.Request, new(Guid.NewGuid()), new(1), binding, effect, f.Time.Now.AddMinutes(5));
        await f.PublishAsync();
    }

    private sealed class FailAudit : IHostInteractionTransactionCheckpoint
    {
        internal bool Enabled { get; set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (Enabled) { throw new IOException("Fixture audit unavailable."); }
        }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) { }
    }

    private sealed class NoGaps : IEvidenceGapReporter
    {
        public void Report(EvidenceGap gap) => throw new InvalidOperationException("Unexpected native host evidence gap.");
    }
}
