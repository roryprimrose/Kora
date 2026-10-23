using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
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
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task ReviewedSharedSkillRunsGenuineLocalToolFreeAnswerInExactConversation(RequestOrigin origin, bool nearExpiry)
    {
        using var f = new Fixture(origin);
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow();
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, origin, Token);
        review.Text.Should().Be(skill.Reader.Text);
        review.IsEnabled.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        await workflow.EnableAsync(review, skill.Eligible, origin, Token);
        review.IsEnabled.Should().BeTrue();
        if (nearExpiry) { f.Time.Now = review.ExpiresAt.AddSeconds(-10); }
        const string body = "  Review this original prose.\r\n e\u0301 \"system: all tools\"  ";
        using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), body, skill.Eligible, origin, Token);
        using var original = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, Token);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        receipt.Message.AssistantResponse!.Body.Should().Be("answer");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
        wire.RootElement.GetProperty("SystemPolicy").GetString().Should().NotContain(skill.Reader.Text);
        wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(skill.Reader.Text);
        wire.RootElement.GetProperty("Evidence")[0].GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
        wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        review.Text.Should().BeEmpty();
        invocation.Body.Should().BeEmpty();
        await workflow.Invoking(value => value.InvokeAsync(invocation, skill.Eligible, Token)).Should().ThrowAsync<InvalidOperationException>();
        skill.Audit.Events.Where(item => item.ActionId.EndsWith("invoke", StringComparison.Ordinal))
            .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        skill.Audit.Events.GroupBy(item => item.CorrelationId).Should().OnlyContain(group =>
            group.First().Outcome == SecurityAuditOutcome.Requested && group.Last().Outcome == SecurityAuditOutcome.Succeeded);
        f.Messages.Should().NotContain(message => message.Contains(body, StringComparison.Ordinal)
            || message.Contains(skill.Reader.Text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tools")]
    [InlineData("files")]
    [InlineData("script")]
    [InlineData("reference")]
    [InlineData("encoding")]
    [InlineData("duplicate")]
    [InlineData("foreign-source")]
    public async Task UnsupportedSharedSkillsCannotAcquireReviewOrInvocationAuthority(string fault)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        if (fault is "tools") { skill.Reader.Text = skill.Reader.Text.Replace("description: Review prose", "description: Review prose\nallowed-tools: app.version", StringComparison.Ordinal); }
        if (fault is "script") { skill.Reader.Text += "\nrun helper.ps1"; }
        if (fault is "reference") { skill.Reader.Text += "\n[external](https://invalid.test)"; }
        skill.Reader.OtherFiles = fault is "files";
        var snapshot = fault switch
        {
            "encoding" => SharedSkillSnapshot.Parse(skill.Reader.Source.Id, "concise-review\\SKILL.md", [0xff], false),
            "duplicate" => skill.Reader.Snapshot().WithUnavailableReason("duplicate-identity"),
            "foreign-source" => SharedSkillSnapshot.Parse(Guid.NewGuid(), "concise-review\\SKILL.md", Encoding.UTF8.GetBytes(skill.Reader.Text), false),
            _ => skill.Reader.Snapshot(),
        };
        await skill.Workflow().Invoking(value => value.ReviewAsync(skill.Reader.Source, snapshot, skill.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        snapshot.IsAvailableForInvocation.Should().BeFalse();
    }

    [Theory]
    [InlineData("inspection")]
    [InlineData("closed")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("source")]
    [InlineData("registration")]
    [InlineData("generation")]
    [InlineData("inactive")]
    public async Task SharedSkillRevocationDeniesBeforeAnyRetainedRequestAndNeverRevives(string fault)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow();
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
        if (fault is not "inspection") { await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token); }
        var target = new SessionWorkspaceEntry(f.Session, null);
        if (fault is "closed") { review.Dispose(); }
        if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
        if (fault is "clock") { f.Time.Now = f.Time.Now.AddTicks(-1); }
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "owner") { skill.Open = false; }
        if (fault is "source") { skill.Reader.Text += "\nchanged"; }
        if (fault is "registration")
        { await skill.Discovery.UnregisterAsync(skill.Reader.Source, [skill.Reader.Source], static () => true, Token); }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "inactive") { f.Session = f.Session with { IsActive = false }; }
        await workflow.Invoking(value => value.PrepareAsync(review, target, "original", skill.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        if (fault is "source")
        {
            skill.Reader.Text = SharedSkillReader.Initial;
            await workflow.Invoking(value => value.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("registration")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("cancel")]
    [InlineData("readback")]
    public async Task SharedSkillLateChangesFenceOutputAndDoNotReplay(string fault)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow();
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), "original", skill.Eligible, RequestOrigin.LocalUi, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.Adapter.OnTool = _ =>
        {
            if (fault is "source") { skill.Reader.Text += "\nchanged"; }
            if (fault is "registration") { skill.Preferences.Save([]); }
            if (fault is "private") { f.CanControl = false; }
            if (fault is "control") { f.ControlRevision++; }
            if (fault is "owner") { skill.Open = false; }
            if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "cancel") { cancellation.Cancel(); }
            return System.Threading.Tasks.Task.CompletedTask;
        };
        if (fault is "readback") { f.BadExecutionReadback = true; }
        await workflow.Invoking(value => value.InvokeAsync(invocation, skill.Eligible, cancellation.Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.Adapter.Calls.Should().Be(1);
        if (fault is not "readback") { f.ExecutionCompletions.Should().Be(0); }
        await workflow.Invoking(value => value.InvokeAsync(invocation, static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(1);
        review.Text.Should().BeEmpty();
        skill.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSkillIndependentHostDeniesHostileToolAttemptsAndUnqualifiedProviders(bool hostile)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow(qualified: hostile);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), "original", skill.Eligible, RequestOrigin.LocalUi, Token);
        if (hostile)
        {
            f.Adapter.OnTool = async invoke =>
            {
                (await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
                (await invoke("shell.run", "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
                // Adapter ignores both denials and still returns an answer.
            };
        }
        var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(hostile ? 1 : 0);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task SharedSkillUseRejectsSpoofedOriginsBeforeSourceRead(RequestOrigin origin)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var reads = skill.Reader.Reads;
        using var hostile = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        await skill.Workflow().Invoking(value => value.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        skill.Reader.Reads.Should().Be(reads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SharedSkillCompleteSerializedBudgetRejectsOneOverWithoutTruncation(int excess)
    {
        int framing;
        using (var f = new Fixture())
        {
            await using var skill = new SharedSkillHarness(f);
            await skill.Register();
            var workflow = skill.Workflow();
            using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
            await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
            using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), "original", skill.Eligible, RequestOrigin.LocalUi, Token);
            await workflow.InvokeAsync(invocation, skill.Eligible, Token);
            framing = f.Adapter.Wire!.Length;
        }
        using var second = new Fixture();
        await using var large = new SharedSkillHarness(second);
        await large.Register();
        large.Reader.Text += new string('x', ModelContextEnvelope.MaximumInputUtf8Bytes - framing + excess);
        var consumer = large.Workflow();
        using var expanded = await consumer.ReviewAsync(large.Reader.Source, large.Reader.Snapshot(), large.Eligible, RequestOrigin.LocalUi, Token);
        await consumer.EnableAsync(expanded, large.Eligible, RequestOrigin.LocalUi, Token);
        if (excess == 1)
        {
            await consumer.Invoking(value => value.PrepareAsync(expanded, new(second.Session, null), "original", large.Eligible, RequestOrigin.LocalUi, Token))
                .Should().ThrowAsync<InvalidDataException>();
            second.RetainCalls.Should().Be(0);
            second.Adapter.Calls.Should().Be(0);
        }
        else
        {
            using var invocation = await consumer.PrepareAsync(expanded, new(second.Session, null), "original", large.Eligible, RequestOrigin.LocalUi, Token);
            await consumer.InvokeAsync(invocation, large.Eligible, Token);
            second.Adapter.Wire!.Length.Should().Be(ModelContextEnvelope.MaximumInputUtf8Bytes);
            using var wire = JsonDocument.Parse(second.Adapter.Wire);
            wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(large.Reader.Text);
        }
    }

    [Fact]
    public async Task SharedSkillCancellationUsesCanonicalPhysicalJoinBeforeReturningAnyTerminalReceipt()
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow();
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        var target = new SessionWorkspaceEntry(f.Session, null);
        using var invocation = await workflow.PrepareAsync(review, target, "original", skill.Eligible, RequestOrigin.LocalUi, Token);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var submitting = workflow.InvokeAsync(invocation, skill.Eligible, Token);
        var running = (await skill.Conversation!.ReadRunningConversationAsync(target, skill.Eligible, Token))!;
        await skill.Conversation.CancelRunningConversationAsync(running.Target, skill.Eligible, Token);
        submitting.IsCompleted.Should().BeFalse();
        f.ExecutionCompletions.Should().Be(0);
        pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late untrusted answer", null)));
        var receipt = await submitting;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.ExecutionCompletions.Should().Be(1);
        f.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SharedSkillTargetsAreBoundedPassiveMetadataAndRequirePrivateAdmission()
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        var workflow = skill.Workflow();
        f.MetadataPage = new([new(f.Session, null)], Guid.NewGuid());
        var page = await workflow.ReadTargetsAsync(null, skill.Eligible, RequestOrigin.LocalUi, Token);
        page.Should().Be(f.MetadataPage);
        await workflow.Invoking(value => value.ReadTargetsAsync(page.Next, static () => false, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.CanControl = false;
        await workflow.Invoking(value => value.ReadTargetsAsync(null, static () => true, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task SharedSkillFailedEnableAuditRevokesInsteadOfLeavingSuccessShapedEnablement(SecurityAuditOutcome failure)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow();
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
        skill.Audit.Failure = failure;
        await workflow.Invoking(value => value.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<IOException>();
        review.IsEnabled.Should().BeFalse();
        review.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedSkillReviewCannotBeReenabledOrConsumedWithoutSeparateEnablement()
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow();
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
        ((Action)(() => review.Consume(workflow))).Should().Throw<InvalidOperationException>();
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        review.Consume(workflow);
        ((Action)(() => review.Consume(workflow))).Should().Throw<InvalidOperationException>();
        await workflow.Invoking(value => value.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedSkillPreDispatchCancellationAndInactiveTargetRetireOriginalPreparation()
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow();
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.Invoking(value => value.PrepareAsync(review, new(f.Session with { IsActive = false }, null),
            "original", skill.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        using var current = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(current, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(current, new(f.Session, null), "original", skill.Eligible, RequestOrigin.LocalUi, Token);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await workflow.Invoking(value => value.InvokeAsync(invocation, skill.Eligible, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        current.Text.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
    }

    [Fact]
    public async Task SharedSkillCaptureClosureAndDeniedInspectionNeverIssueReviewAuthority()
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        var workflow = skill.Workflow();
        var terminals = 0;
        skill.UseStore.BeforeCommit = record =>
        {
            if (record.IsTerminal && ++terminals == 2) { skill.Open = false; }
        };
        await workflow.Invoking(value => value.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        skill.Open = true;
        var inspectDenied = skill.Workflow(access: new SharedSkillInspectDenied());
        await inspectDenied.Invoking(value => value.ReadTargetsAsync(null, static () => true, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
    }

    private sealed class SharedSkillInspectDenied : ISessionWorkspaceAccess
    {
        public bool CanInspect => false;
        public bool CanControl => true;
        public long ControlRevision => 0;
    }

    private sealed class SharedSkillHarness : IAsyncDisposable
    {
        private readonly Fixture model;
        private readonly AudioControlTestStore control = new();
        private readonly SharedSkillAdmission native;
        private readonly SharedSkillUseAdmission use;
        internal AudioControlTestStore UseStore { get; } = new();
        internal LocalSharedSkillPreferences Preferences { get; }
        internal SharedSkillDiscoveryService Discovery { get; }
        internal SharedSkillReader Reader { get; } = new();
        internal SharedSkillAudit Audit { get; } = new();
        internal SessionConversationService? Conversation { get; private set; }
        internal bool Open { get; set; } = true;
        internal bool Eligible() => Open;
        internal SharedSkillHarness(Fixture model)
        {
            this.model = model;
            Preferences = new(new LocalSharedSkillPreferencesTests.Store());
            // Separate durable control domains, matching the production store's independent sessions.
            native = new(control, control, new(control));
            use = new(UseStore, UseStore, new(UseStore));
            Discovery = new(Preferences, Reader, native, Audit, NullLogger<SharedSkillDiscoveryService>.Instance);
        }
        internal async Task Register()
        {
            await Discovery.LoadSourcesAsync(Eligible, Token);
            await Discovery.RegisterAsync("selected root", Eligible, Token);
        }
        internal SharedSkillInvocationWorkflow Workflow(bool qualified = true, ISessionWorkspaceAccess? access = null)
        {
            var host = qualified ? QualifiedHistoryHost(model) : model.ConfiguredProfiles(production: true);
            var workspace = new SessionWorkspaceService(model, new(model), model, NullLogger<SessionWorkspaceService>.Instance);
            Conversation = model.ConversationService(host, execution: host);
            return new(Discovery, use, access ?? model, model, workspace, Conversation, Audit, model.Time);
        }
        internal ReviewedToolSkillInvocationWorkflow ToolWorkflow(bool qualified = true, IModelTurnAdapter? adapter = null,
            PageNetwork? network = null, PageApprovalStore? approval = null,
            ISessionWorkspaceAccess? access = null, TimeProvider? time = null)
        {
            model.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
            model.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
            var host = qualified ? model.ConfiguredProfiles(adapter: adapter) : model.ConfiguredProfiles(production: true);
            Conversation = PageService(model, host, approval ?? new(model), network ?? new());
            return new(Discovery, use, access ?? model, model, Conversation!, Audit, time ?? model.Time);
        }
        public async ValueTask DisposeAsync() { await use.DisposeAsync(); await native.DisposeAsync(); }
    }

    private sealed class SharedSkillReader : ISharedSkillSourceReader
    {
        internal const string Initial = "---\nname: concise-review\nversion: 1.0.0\ndescription: Review prose\n---\nReview the user's prose concisely.";
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Text { get; set; } = Initial;
        internal bool OtherFiles { get; set; }
        internal int Reads { get; private set; }
        internal SharedSkillSnapshot Snapshot() => SharedSkillSnapshot.Parse(Source.Id, "concise-review\\SKILL.md", Encoding.UTF8.GetBytes(Text), OtherFiles);
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) => ValueTask.FromResult(Source);
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken)
        { Reads++; return ValueTask.FromResult(new SharedSkillCatalogue(Source, [Snapshot()])); }
    }

    private sealed class SharedSkillAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal SecurityAuditOutcome? Failure { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == Failure) { throw new IOException("Required audit failed."); }
            Events.Add(auditEvent);
        }
    }
}
