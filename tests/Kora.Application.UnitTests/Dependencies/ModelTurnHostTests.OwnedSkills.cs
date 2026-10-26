using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task OwnedSkillSeparateReviewEnablePreviewAndUseRetainExactLocalAnswerOnce(RequestOrigin origin, bool nearExpiry)
    {
        using var f = new Fixture(origin);
        await using var skill = new OwnedSkillHarness(f, origin);
        using var review = await skill.Review();
        review.Text.Should().Contain(skill.Store.Snapshot!.Document.ManifestText).And.Contain(skill.Store.Snapshot.Document.Guidance);
        review.IsEnabled.Should().BeFalse();
        skill.Store.Snapshot.Document.IsAvailableForInvocation.Should().BeFalse();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        review.IsEnabled.Should().BeTrue();
        if (nearExpiry) { f.Time.Now = review.ExpiresAt.AddSeconds(-10); }
        const string body = "  original unchanged e\u0301\r\n owned skill invoke  ";
        using var invocation = await skill.Prepare(review, body);
        invocation.Body.Should().Be(body);
        invocation.Origin.Should().Be(origin);
        using (var preview = JsonDocument.Parse(invocation.Preview))
        {
            preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
            preview.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(skill.Store.Snapshot.Document.ManifestText);
            preview.RootElement.GetProperty("Evidence")[1].GetProperty("Content").GetString().Should().Be(skill.Store.Snapshot.Document.Guidance);
            preview.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        }
        f.Adapter.Calls.Should().Be(0);
        var receipt = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("answer");
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(2);
        wire.RootElement.GetProperty("Evidence").EnumerateArray().Should().OnlyContain(
            item => item.GetProperty("Disclosure").GetInt32() == (int)ModelEvidenceDisclosure.LocalOnly);
        wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        review.Text.Should().BeEmpty();
        review.IsEnabled.Should().BeFalse();
        invocation.Body.Should().BeEmpty();
        invocation.Preview.Should().BeEmpty();
        await skill.Workflow.Invoking(flow => flow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(1);
        skill.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Messages.Should().NotContain(text => text.Contains(body, StringComparison.Ordinal)
            || text.Contains(skill.Store.Snapshot.Document.Guidance, StringComparison.Ordinal));
        skill.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-id")]
    [InlineData("wrong-version")]
    [InlineData("corrupt")]
    [InlineData("private")]
    [InlineData("inspect")]
    [InlineData("owner")]
    [InlineData("channel")]
    [InlineData("cancel")]
    [InlineData("terminal")]
    public async Task OwnedSkillReviewRejectsUnavailableExactSourceAndOriginalAuthority(string fault)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f, inspect: fault is not "inspect");
        if (fault is "missing") { skill.Store.Snapshot = null; }
        if (fault is "wrong-id" or "wrong-version")
        {
            skill.Store.Snapshot = skill.Store.Snapshot! with
            {
                Document = OwnedDeclarativeSkillDocument.Create(fault is "wrong-id" ? "another" : "example",
                    fault is "wrong-version" ? "2.0.0" : "1.0.0", "Example", "Description", "literal"),
            };
        }
        if (fault is "corrupt") { skill.Store.Failure = new InvalidDataException("corrupt private package"); }
        if (fault is "private") { f.CanControl = false; }
        if (fault is "owner") { skill.Open = false; }
        var terminals = 0;
        if (fault is "terminal") { skill.Control.BeforeCommit = record => { if (record.IsTerminal && ++terminals == 2) { skill.Open = false; } }; }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancel") { cancel.Cancel(); }
        using var hostile = fault is "channel"
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request) : null;
        await skill.Workflow.Invoking(flow => flow.ReviewAsync("example", "1.0.0", skill.Authority, skill.Eligible, cancel.Token))
            .Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("manifest-identity")]
    [InlineData("instructions-identity")]
    [InlineData("manifest-bytes")]
    [InlineData("instructions-bytes")]
    [InlineData("missing")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("closed")]
    [InlineData("foreign")]
    [InlineData("audit-request")]
    [InlineData("audit-success")]
    [InlineData("double-enable")]
    public async Task OwnedSkillEnableRejectsChangedSourceAndCannotRenewAuthority(string fault)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        using var review = await skill.Review();
        var before = skill.Store.Snapshot;
        ChangeOwnedSkill(fault, skill, f, review);
        if (fault is "audit-request") { skill.Audit.Failure = SecurityAuditOutcome.Requested; }
        if (fault is "audit-success") { skill.Audit.Failure = SecurityAuditOutcome.Succeeded; }
        if (fault is "double-enable") { await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token); }
        await using var other = new OwnedSkillHarness(f);
        var flow = fault is "foreign" ? other.Workflow : skill.Workflow;
        await flow.Invoking(value => value.EnableAsync(review, skill.Authority, skill.Eligible, Token))
            .Should().ThrowAsync<Exception>();
        review.IsCurrent.Should().BeFalse();
        skill.Store.Snapshot = before;
        await skill.Workflow.Invoking(value => value.EnableAsync(review, skill.Authority, skill.Eligible, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("not-enabled")]
    [InlineData("inactive")]
    [InlineData("generation")]
    [InlineData("wrong-session")]
    [InlineData("request")]
    [InlineData("owner")]
    public async Task OwnedSkillPreparationRequiresEnabledRevisionOriginalRequestAndExactActiveTarget(string fault)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        using var review = await skill.Review();
        if (fault is not "not-enabled") { await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token); }
        var target = new SessionWorkspaceEntry(f.Session, null);
        if (fault is "inactive") { target = new(f.Session with { IsActive = false }, null); }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "wrong-session") { target = new(f.Session with { SessionId = new(Guid.NewGuid()) }, null); }
        if (fault is "owner") { skill.Open = false; }
        await skill.Workflow.Invoking(flow => flow.PrepareAsync(review, target, fault is "request" ? "" : "original",
            skill.Authority, skill.Eligible, Token)).Should().ThrowAsync<Exception>();
        review.IsCurrent.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16000)]
    public async Task OwnedSkillCompleteSerializedBudgetNeverTruncatesValidSavedPackages(int excess)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        using var initial = await skill.Review();
        await skill.Workflow.EnableAsync(initial, skill.Authority, skill.Eligible, Token);
        using var preview = await skill.Prepare(initial);
        var framing = Encoding.UTF8.GetByteCount(preview.Preview);
        var document = skill.Store.Snapshot!.Document;
        skill.Store.Snapshot = skill.Store.Snapshot with
        {
            Document = OwnedDeclarativeSkillDocument.Create(document.Id, document.Version, document.Name, document.Description,
                document.Guidance + new string('x', ModelContextEnvelope.MaximumInputUtf8Bytes - framing + excess)),
        };
        using var review = await skill.Review();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        if (excess != 0)
        {
            await skill.Invoking(value => value.Prepare(review)).Should().ThrowAsync<InvalidDataException>();
            f.RetainCalls.Should().Be(0);
            f.Adapter.Calls.Should().Be(0);
        }
        else
        {
            using var invocation = await skill.Prepare(review);
            Encoding.UTF8.GetByteCount(invocation.Preview).Should().Be(32768);
            await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
            f.Adapter.Wire!.Length.Should().Be(32768);
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("cancel")]
    [InlineData("readback")]
    [InlineData("expiry")]
    public async Task OwnedSkillLateChangesSuppressOutputWithoutReplay(string fault)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        using var review = await skill.Review();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Prepare(review);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.Adapter.OnTool = _ =>
        {
            if (fault is "source") { ChangeOwnedSkill("instructions-bytes", skill, f, review); }
            if (fault is "privacy") { f.CanControl = false; }
            if (fault is "control") { f.ControlRevision++; }
            if (fault is "owner") { skill.Open = false; }
            if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "cancel") { cancel.Cancel(); }
            if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
            return Task.CompletedTask;
        };
        if (fault is "readback") { f.BadExecutionReadback = true; }
        await skill.Workflow.Invoking(flow => flow.InvokeAsync(invocation, skill.Authority, skill.Eligible, cancel.Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.Adapter.Calls.Should().Be(1);
        if (fault is not "readback") { f.ExecutionCompletions.Should().Be(0); }
        review.Text.Should().BeEmpty();
        skill.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSkillUnavailableProviderAndForbiddenToolAttemptsRetainTruthfulTerminal(bool hostile)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f, qualified: hostile);
        using var review = await skill.Review();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Prepare(review);
        if (hostile)
        {
            f.Adapter.OnTool = async invoke =>
            {
                (await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
                (await invoke("shell.run", "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
            };
        }
        var receipt = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(hostile ? 1 : 0);
        f.RetainCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSkillCancellationJoinsRealReadOrModelDespiteIgnoredCancellation(bool model)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!model)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            skill.Store.DuringRead = async () => { entered.SetResult(); await release.Task; };
            var reading = skill.Workflow.ReviewAsync("example", "1.0.0", skill.Authority, skill.Eligible, cancellation.Token);
            await entered.Task.WaitAsync(Token);
            cancellation.Cancel();
            reading.IsCompleted.Should().BeFalse();
            release.SetResult();
            await ((Func<Task>)(async () => await reading)).Should().ThrowAsync<OperationCanceledException>();
            f.Adapter.Calls.Should().Be(0);
        }
        else
        {
            using var review = await skill.Review();
            await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
            using var invocation = await skill.Prepare(review);
            var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.OnRun = () => entered.SetResult();
            f.Adapter.Pending = pending.Task;
            var running = skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, cancellation.Token);
            await entered.Task.WaitAsync(Token);
            cancellation.Cancel();
            running.IsCompleted.Should().BeFalse();
            pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late answer", null)));
            await ((Func<Task>)(async () => await running)).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            f.ExecutionCompletions.Should().Be(0);
            f.Adapter.Calls.Should().Be(1);
        }
    }

    [Fact]
    public async Task OwnedSkillTargetPagesAndPreAttemptCancellationPreserveOriginalAuthority()
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        f.MetadataPage = new([new(f.Session, null)], null);
        (await skill.Workflow.ReadTargetsAsync(null, skill.Authority, skill.Eligible, Token)).Records.Should().ContainSingle();
        using var review = await skill.Review();
        ((Action)(() => review.Consume(skill.Workflow))).Should().Throw<InvalidOperationException>();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Prepare(review);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await skill.Workflow.Invoking(flow => flow.InvokeAsync(invocation, skill.Authority, skill.Eligible, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        review.IsCurrent.Should().BeFalse();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSkillCancellationDuringFinalAdmissionOrRunningTurnIsTruthfulAndJoined(bool runningTurn)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        using var review = await skill.Review();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Prepare(review);
        if (!runningTurn)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
            skill.Store.DuringRead = () => { cancel.Cancel(); return Task.CompletedTask; };
            await skill.Workflow.Invoking(flow => flow.InvokeAsync(invocation, skill.Authority, skill.Eligible, cancel.Token))
                .Should().ThrowAsync<OperationCanceledException>();
            f.Adapter.Calls.Should().Be(0);
            f.RetainCalls.Should().Be(0);
        }
        else
        {
            var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.Pending = pending.Task;
            var submitting = skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
            var running = (await skill.Conversation.ReadRunningConversationAsync(invocation.Target, skill.Eligible, Token))!;
            await skill.Conversation.CancelRunningConversationAsync(running.Target, skill.Eligible, Token);
            submitting.IsCompleted.Should().BeFalse();
            pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late", null)));
            var receipt = await submitting;
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            receipt.Message.AssistantResponse.Should().BeNull();
            f.Adapter.Calls.Should().Be(1);
        }
        review.IsCurrent.Should().BeFalse();
    }

    private static void ChangeOwnedSkill(string fault, OwnedSkillHarness skill, Fixture f, OwnedSkillUseReview review)
    {
        var snapshot = skill.Store.Snapshot!;
        switch (fault)
        {
            case "directory": skill.Store.Snapshot = snapshot with { DirectoryIdentity = "replaced" }; break;
            case "manifest-identity": skill.Store.Snapshot = snapshot with { ManifestIdentity = "replaced" }; break;
            case "instructions-identity": skill.Store.Snapshot = snapshot with { InstructionsIdentity = "replaced" }; break;
            case "manifest-bytes":
            case "instructions-bytes":
                skill.Store.Snapshot = snapshot with
                {
                    Document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0",
                        fault is "manifest-bytes" ? "Changed" : "Example", "Description",
                        fault is "instructions-bytes" ? "changed" : "literal guidance"),
                };
                break;
            case "missing": skill.Store.Snapshot = null; break;
            case "expiry": f.Time.Now = review.ExpiresAt; break;
            case "clock": f.Time.Now = f.Time.Now.AddTicks(-1); break;
            case "privacy": f.CanControl = false; break;
            case "control": f.ControlRevision++; break;
            case "owner": skill.Open = false; break;
            case "closed": review.Dispose(); break;
        }
    }

    private sealed class OwnedSkillHarness : IAsyncDisposable
    {
        internal OwnedControlStore Control { get; } = new();
        private readonly OwnedSkillControlAdmission admission;
        private readonly Fixture model;
        internal OwnedPackageStore Store { get; } = new();
        internal SharedSkillAudit Audit { get; } = new();
        internal OwnedSkillAuthoringAuthority Authority { get; }
        internal OwnedSkillInvocationWorkflow Workflow { get; }
        internal OwnedSkillSaveWorkflow Saved { get; }
        internal SessionConversationService Conversation { get; }
        internal bool Open { get; set; } = true;
        internal bool Eligible() => Open;
        internal OwnedSkillHarness(Fixture model, RequestOrigin origin = RequestOrigin.LocalUi, bool qualified = true, bool inspect = true)
        {
            this.model = model;
            Authority = new(origin, Eligible);
            admission = new(Control, Control, new(Control));
            Saved = new(Store, admission, Audit);
            var host = qualified ? QualifiedHistoryHost(model) : model.ConfiguredProfiles(production: true);
            var workspace = new SessionWorkspaceService(model, new(model), model, NullLogger<SessionWorkspaceService>.Instance);
            Conversation = model.ConversationService(host, execution: host);
            Workflow = new(Store, admission, inspect ? model : new SharedSkillInspectDenied(), model, workspace,
                Conversation, Audit, model.Time);
        }
        internal Task<OwnedSkillUseReview> Review() => Workflow.ReviewAsync("example", "1.0.0", Authority, Eligible, Token);
        internal Task<OwnedSkillInvocation> Prepare(OwnedSkillUseReview review, string body = "original") =>
            Workflow.PrepareAsync(review, new(model.Session, null), body, Authority, Eligible, Token);
        public ValueTask DisposeAsync() => admission.DisposeAsync();
    }

    private sealed class OwnedControlStore : AudioControlTestStore, IOwnedSkillControlSessionStore
    {
        public ValueTask<WorkSessionAuthorization> CreateOwnedSkillSession(HostRequest request, Func<bool> admitted, CancellationToken token) =>
            CreateAudioControlSessionAsync(request, admitted, token);
        public ValueTask<T> WithOwnedSkillSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token) =>
            WithAudioControlSessionAsync(request, generation, operation, token);
    }

    private sealed class OwnedPackageStore : IOwnedDeclarativeSkillStore
    {
        internal OwnedDeclarativeSkillSnapshot? Snapshot { get; set; } = new(
            OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "literal guidance"),
            "directory", "manifest", "instructions");
        internal Func<Task>? DuringRead { get; set; }
        internal Exception? Failure { get; set; }
        internal int Writes { get; private set; }
        internal Func<Task>? DuringList { get; set; }
        internal int Lists { get; private set; }
        public string Destination(OwnedDeclarativeSkillDocument document) => Path.Combine(Environment.CurrentDirectory, "owned");
        public async Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(CancellationToken token)
        {
            Lists++;
            if (DuringList is { } during) { await during(); }
            if (Failure is not null) { throw Failure; }
            return Snapshot is null ? [] : [Snapshot];
        }
        public async Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token)
        {
            if (DuringRead is { } during) { await during(); }
            if (Failure is not null) { throw Failure; }
            return Snapshot;
        }
        public Task<OwnedDeclarativeSkillSnapshot> Publish(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
            Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token)
        { Writes++; throw new InvalidOperationException("Invocation must never publish."); }
    }
}
