using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.Tools;
using Kora.Core.Customizations;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Skills;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task SelectedRegisteredSkillCompletesUnchangedTaskThroughDistinctReviewsAndCanonicalLocalExecutor(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, shared.Register);
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        const string task = "  Review my original prose e\u0301.\r\nDo not rewrite this task.  ";
        using var snapshot = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null),
            task, new(origin, shared.Eligible));
        shared.Reader.ExactReads.Should().Be(0);
        shared.Reader.Reads.Should().Be(0);
        shared.Audit.Events.Should().NotContain(item => item.ActionId.StartsWith("skills.revision.", StringComparison.Ordinal));
        var transport = new ToolTransport(CustomizationListCall, SkillSelectionAnswer(snapshot.Records[0].Ref),
            """{"kind":"answer","answer":"Genuine skill-produced answer for the original prose."}""");
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        var conversation = f.ConversationService(host, execution: host);
        var workflow = shared.Workflow(conversation: conversation);
        var selected = await conversation.SubmitCustomizationDiscovery(snapshot, shared.Eligible, Token);
        selected.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        selected.Message.AssistantResponse!.Body.Should().Contain("No skill guidance was read, enabled or invoked");
        host.IsQuiescent.Should().BeTrue();
        var proposal = snapshot.SkillProposal!;
        proposal.IsCurrent.Should().BeTrue();
        shared.Reader.ExactReads.Should().Be(0);
        f.ExecutionCompletions.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
        using var review = await ReadSelectedSkill(workflow, proposal, shared.Eligible);
        review.IsEnabled.Should().BeFalse();
        review.Text.Should().Be(shared.Reader.Text);
        f.ExecutionStarts.Should().Be(1);
        await workflow.EnableAsync(review, shared.Eligible, origin, Token);
        using var invocation = await workflow.PrepareAsync(review, proposal.Target, proposal.OriginalRequest, shared.Eligible, origin, Token);
        invocation.Body.Should().Be(task);
        transport.Requests.Should().HaveCount(2);
        var receipt = await workflow.InvokeAsync(invocation, shared.Eligible, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("Genuine skill-produced answer for the original prose.");
        receipt.Message.Body.Should().Be(task);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        host.IsQuiescent.Should().BeTrue();
        transport.Requests.Should().HaveCount(3);
        var executor = transport.Requests[2];
        executor.Context.UserRequest.Should().Be(task);
        executor.Context.ToolMode.Should().Be(ModelToolMode.None);
        executor.Context.Tools.Should().BeEmpty();
        executor.Messages.Should().BeEmpty("the joined selector is not a parent runtime or fabricated tool result");
        executor.Context.Evidence.Should().ContainSingle().Which.Content.Should().Be(shared.Reader.Text);
        transport.Requests.Take(2).Should().OnlyContain(request => request.Context.ToolMode == ModelToolMode.CustomizationDiscovery
            && string.Equals(request.Context.UserRequest, task, StringComparison.Ordinal)
            && request.Context.Evidence.All(item => !item.Content.Contains(shared.Reader.Text, StringComparison.Ordinal)));
        var retained = await conversation.ReadPageAsync(f.Session.SessionId, null, 25, Token);
        retained.Messages.Should().ContainSingle().Which.Should().Be(receipt.Message);
        f.RetainCalls.Should().Be(2);
        shared.Reader.Reads.Should().Be(0, "selection must never discover/read unrelated package bodies");
        shared.Audit.Events.Where(item => string.Equals(item.ActionId, "skills.revision.invoke", StringComparison.Ordinal)).Select(item => item.Outcome)
            .Should().Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        await workflow.Invoking(value => value.InvokeAsync(invocation, shared.Eligible, Token)).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(async () => await ReadSelectedSkill(workflow, proposal, shared.Eligible)))
            .Should().ThrowAsync<InvalidOperationException>();
        f.ExecutionStarts.Should().Be(2);
        f.Messages.Should().NotContain(message => message.Contains(task, StringComparison.Ordinal)
            || message.Contains(shared.Reader.Text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unserved")]
    [InlineData("foreign")]
    [InlineData("wrong-kind")]
    [InlineData("disabled")]
    [InlineData("syntax")]
    [InlineData("task")]
    [InlineData("retired")]
    [InlineData("source")]
    public async Task InvalidSelectedSkillNeverReadsEnablesOrInvokesGuidance(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        using var reviewed = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, shared.Eligible));
        using var snapshot = fault is "wrong-kind" or "disabled"
            ? new CustomizationDiscoverySnapshot(sources, catalogue, use, [sources.Values[0]],
                reviewed.Records.Select(record => fault is "wrong-kind" ? record with { Kind = CustomizationKind.Agent }
                    : record with { Enabled = false }).ToImmutableArray(),
                reviewed.Target, reviewed.OriginalRequest, reviewed.Origin, shared.Eligible, f.Time,
                skillBindings: [shared.Reader.Binding])
            : reviewed;
        var reference = fault is "foreign" ? Guid.NewGuid().ToString("N") : snapshot.Records[0].Ref;
        var answer = fault is "syntax" ? "{\"skillRef\":\"a model path\"}"
            : fault is "task" ? "{\"skillRef\":\"" + reference + "\",\"task\":\"new task\"}"
            : JsonSerializer.Serialize(new { skillRef = reference });
        var transport = fault is "unserved" ? new ToolTransport(SkillSelectionAnswer(reference))
            : new ToolTransport(CustomizationListCall, JsonSerializer.Serialize(new { kind = "answer", answer }));
        if (fault is "retired" or "source")
        {
            transport.AfterRequest = _ =>
            {
                if (fault is "retired") { snapshot.Retire(); }
                else { sources.Values[0] = sources.Values[0] with { Revision = "new-native-revision" }; }
            };
        }
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        var submit = () => f.ConversationService(host, execution: host).SubmitCustomizationDiscovery(snapshot, shared.Eligible, Token);
        var receipt = await submit();
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        snapshot.SkillProposal.Should().BeNull();
        shared.Reader.ExactReads.Should().Be(0);
        shared.Reader.Reads.Should().Be(0);
        shared.Audit.Events.Should().NotContain(item => item.ActionId.StartsWith("skills.revision.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("native")]
    [InlineData("bytes")]
    [InlineData("metadata")]
    [InlineData("registration")]
    [InlineData("privacy")]
    [InlineData("generation")]
    [InlineData("retired")]
    [InlineData("expiry")]
    [InlineData("files")]
    [InlineData("tools")]
    [InlineData("inputs")]
    [InlineData("references")]
    [InlineData("foreign")]
    [InlineData("owner-after-metadata")]
    public async Task SelectedSkillSourceAndOriginalAuthorityAreRevalidatedBeforeReadAndEveryUse(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        using var snapshot = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, shared.Eligible));
        var transport = new ToolTransport(CustomizationListCall, SkillSelectionAnswer(snapshot.Records[0].Ref));
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        var conversation = f.ConversationService(host, execution: host);
        await conversation.SubmitCustomizationDiscovery(snapshot, shared.Eligible, Token);
        var workflow = shared.Workflow(conversation: conversation);
        if (fault is "native") { shared.Reader.NativeRevision++; }
        if (fault is "bytes") { shared.Reader.Text += "\nchanged"; }
        if (fault is "metadata") { sources.Values[0] = sources.Values[0] with { Revision = "restored-bytes-new-native-revision" }; }
        if (fault is "registration") { await shared.Discovery.UnregisterAsync(shared.Reader.Source, [shared.Reader.Source], shared.Eligible, Token); }
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "retired") { snapshot.Retire(); }
        if (fault is "expiry") { f.Time.Now = snapshot.ExpiresAt; }
        if (fault is "files") { shared.Reader.OtherFiles = true; }
        if (fault is "tools") { shared.Reader.Text = shared.Reader.Text.Replace("---\nReview", "allowed-tools: app.version\n---\nReview", StringComparison.Ordinal); }
        if (fault is "inputs") { shared.Reader.Text = shared.Reader.Text.Replace("---\nReview", "required-inputs: task\n---\nReview", StringComparison.Ordinal); }
        if (fault is "references") { shared.Reader.Text += "\nRead [reference](notes.md)."; }
        if (fault is "foreign") { shared.Reader.WrongSource = true; }
        if (fault is "owner-after-metadata") { sources.AfterRead = () => shared.Open = false; }
        await ((Func<Task>)(async () => await ReadSelectedSkill(workflow, snapshot.SkillProposal!, shared.Eligible)))
            .Should().ThrowAsync<Exception>();
        transport.Requests.Should().HaveCount(2);
        shared.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "skills.revision.enable", StringComparison.Ordinal)
            || string.Equals(item.ActionId, "skills.revision.invoke", StringComparison.Ordinal));
        shared.Reader.Reads.Should().Be(0);
        f.RetainCalls.Should().Be(1, "only the genuine selection terminal was retained");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedSkillSelectorAndExecutorCancellationKeepPhysicalOwnershipUntilJoinedAndSuppressLateOutput(bool executor)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        using var snapshot = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, shared.Eligible));
        var transport = new HeldSelectedSkillTransport(snapshot.Records[0].Ref, executor);
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        var conversation = f.ConversationService(host, execution: host);
        var pending = conversation.SubmitCustomizationDiscovery(snapshot, shared.Eligible, Token);
        SharedSkillUseReview? executionReview = null;
        if (executor)
        {
            (await pending).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            host.IsQuiescent.Should().BeTrue();
            var workflow = shared.Workflow(conversation: conversation);
            executionReview = await ReadSelectedSkill(workflow, snapshot.SkillProposal!, shared.Eligible);
            await workflow.EnableAsync(executionReview, shared.Eligible, RequestOrigin.LocalUi, Token);
            var invocation = await workflow.PrepareAsync(executionReview, snapshot.Target, snapshot.OriginalRequest, shared.Eligible, RequestOrigin.LocalUi, Token);
            pending = workflow.InvokeAsync(invocation, shared.Eligible, Token);
        }
        ConversationSubmissionReceipt receipt;
        try
        {
            await transport.Entered.Task.WaitAsync(Token);
            var running = await conversation.ReadRunningConversationAsync(snapshot.Target, shared.Eligible, Token);
            await conversation.CancelRunningConversationAsync(running!.Target, shared.Eligible, Token);
            await transport.Cancelled.Task.WaitAsync(Token);
            pending.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            transport.Joined.Should().BeFalse();
            if (!executor)
            {
                snapshot.SkillProposal.Should().BeNull();
                shared.Reader.ExactReads.Should().Be(0);
                shared.Audit.Events.Should().NotContain(item => item.ActionId.StartsWith("skills.revision.", StringComparison.Ordinal));
            }
        }
        finally
        {
            transport.Release.TrySetResult();
            receipt = await pending;
            executionReview?.Dispose();
        }
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        transport.Joined.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        f.ExecutionStarts.Should().Be(executor ? 2 : 1);
        shared.Reader.Reads.Should().Be(0);
        if (executor)
        { shared.Audit.Events.Last(item => item.ActionId.EndsWith("invoke", StringComparison.Ordinal)).Outcome
            .Should().Be(Kora.Core.Auditing.SecurityAuditOutcome.Failed); }
        else { snapshot.SkillProposal.Should().BeNull(); }
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("prepare")]
    [InlineData("dispatch")]
    [InlineData("terminal")]
    public async Task SelectedSkillNativeRevisionAndMetadataAreRecheckedAcrossEveryAwaitedUseBoundary(string stage)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        using var snapshot = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, shared.Eligible));
        var transport = new ToolTransport(CustomizationListCall, SkillSelectionAnswer(snapshot.Records[0].Ref), FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        var conversation = f.ConversationService(host, execution: host);
        await conversation.SubmitCustomizationDiscovery(snapshot, shared.Eligible, Token);
        var workflow = shared.Workflow(conversation: conversation);
        using var review = await ReadSelectedSkill(workflow, snapshot.SkillProposal!, shared.Eligible);
        if (stage is "enable")
        {
            shared.Reader.NativeRevision++;
            await workflow.Invoking(value => value.EnableAsync(review, shared.Eligible, RequestOrigin.LocalUi, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            review.IsEnabled.Should().BeFalse();
        }
        else
        {
            await workflow.EnableAsync(review, shared.Eligible, RequestOrigin.LocalUi, Token);
            if (stage is "prepare")
            {
                sources.Values[0] = sources.Values[0] with { Revision = "new-native-revision" };
                await workflow.Invoking(value => value.PrepareAsync(review, snapshot.Target, snapshot.OriginalRequest,
                    shared.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
            }
            else
            {
                using var invocation = await workflow.PrepareAsync(review, snapshot.Target, snapshot.OriginalRequest,
                    shared.Eligible, RequestOrigin.LocalUi, Token);
                if (stage is "dispatch") { shared.Reader.NativeRevision++; }
                else { transport.AfterRequest = _ => sources.Values[0] = sources.Values[0] with { Revision = "new-native-revision" }; }
                var invoke = () => workflow.InvokeAsync(invocation, shared.Eligible, Token);
                if (stage is "dispatch") { await invoke.Should().ThrowAsync<InvalidOperationException>(); }
                else { await invoke.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>(); }
            }
        }
        transport.Requests.Should().HaveCount(stage is "terminal" ? 3 : 2);
        f.ExecutionCompletions.Should().Be(1);
        shared.Reader.Reads.Should().Be(0);
        shared.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "skills.revision.invoke", StringComparison.Ordinal)
            && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("original")]
    [InlineData("unavailable")]
    [InlineData("empty")]
    [InlineData("count")]
    [InlineData("kind")]
    [InlineData("file")]
    [InlineData("excluded")]
    [InlineData("invalid")]
    [InlineData("cancel")]
    [InlineData("registration")]
    public async Task SelectedSkillMetadataAdmissionRejectsUnknownOrUnapprovedInputsBeforeAnyModelOrGuidance(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        if (fault is "excluded") { sources.Values[0] = sources.Values[0] with { Source = sources.Values[0].Source with { Id = "raw.source.3" } }; }
        if (fault is "invalid") { sources.Values[0] = sources.Values[0] with { Description = new string('x', 257) }; }
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, fault is "unavailable" ? null : shared.Discovery);
        IReadOnlyList<CustomizationSource> selected = fault is "empty" ? [] : fault is "count"
            ? Enumerable.Repeat(sources.Values[0].Source, 7).ToArray()
            : [sources.Values[0].Source with
                { Kind = fault is "kind" ? CustomizationKind.Agent : CustomizationKind.Skill, PrimaryFile = fault is "file" ? "other.md" : "SKILL.md" }];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancel") { sources.AfterRead = cancellation.Cancel; }
        if (fault is "registration")
        {
            shared.Reader.BeforeBind = () => shared.Preferences.Save(
                [shared.Reader.Source, new(Guid.NewGuid(), ".other\\skills", new string('d', 48))]);
        }
        using var native = HostActivity.BeginRoot(HostRequest.Create(fault is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await metadata.Invoking(value => value.ReviewSkillSelection(selected, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, () => fault is not "original"), shared.Eligible, cancellation.Token)).Should().ThrowAsync<Exception>();
        shared.Reader.ExactReads.Should().Be(0);
        shared.Reader.Reads.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        shared.Audit.Events.Should().NotContain(item => item.ActionId.StartsWith("skills.revision.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("package")]
    [InlineData("bundle")]
    [InlineData("voice")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task SelectedSkillSourceReadRejectsForeignProfileAndFailedOrCancelledExactValidation(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        using var snapshot = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, shared.Eligible));
        var transport = new ToolTransport(CustomizationListCall, SkillSelectionAnswer(snapshot.Records[0].Ref));
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        var conversation = f.ConversationService(host, execution: host);
        await conversation.SubmitCustomizationDiscovery(snapshot, shared.Eligible, Token);
        var workflow = shared.Workflow(conversation: conversation);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        shared.Reader.ExactSnapshot = binding =>
        {
            if (fault is "relative")
            { return SharedSkillSnapshot.Parse(binding.Source.Id, "foreign\\SKILL.md", Encoding.UTF8.GetBytes(shared.Reader.Text), false)
                .WithPackageDirectoryIdentity(binding.PackageIdentity); }
            if (fault is "package") { return shared.Reader.Snapshot().WithPackageDirectoryIdentity(new string('e', 48)); }
            if (fault is "bundle")
            {
                var text = shared.Reader.Text.Replace("description: Review prose", "description: Review prose\nformat: skill-md-instructions-v2\nreferences: notes.md", StringComparison.Ordinal);
                var primary = SharedSkillSnapshot.Parse(binding.Source.Id, binding.RelativeFile, Encoding.UTF8.GetBytes(text), true,
                    ["concise-review\\notes.md"]).WithPackageDirectoryIdentity(binding.PackageIdentity);
                var bundle = SharedSkillReferenceBundle.Create(primary, new Dictionary<string, byte[]>(StringComparer.Ordinal)
                { ["notes.md"] = Encoding.UTF8.GetBytes("Inert notes.") });
                return primary.WithReferenceBundle(bundle);
            }
            if (shared.Reader.ExactReads == 2)
            {
                if (fault is "cancel") { cancellation.Cancel(); }
                else { throw new IOException("Exact second read unavailable."); }
            }
            return shared.Reader.Snapshot().WithPackageDirectoryIdentity(binding.PackageIdentity);
        };
        using var native = HostActivity.BeginRoot(HostRequest.Create(fault is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await workflow.Invoking(value => value.ReviewSelectedAsync(snapshot.SkillProposal!, shared.Eligible, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        transport.Requests.Should().HaveCount(2);
        shared.Audit.Events.Should().NotContain(item => item.ActionId.StartsWith("skills.revision.", StringComparison.Ordinal));
        shared.Reader.Reads.Should().Be(0);
    }

    [Fact]
    public async Task SelectedSkillBytesRemainPinnedAfterReviewEvenIfTheReaderReportsUnchangedNativeIdentity()
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        using var snapshot = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, shared.Eligible));
        var transport = new ToolTransport(CustomizationListCall, SkillSelectionAnswer(snapshot.Records[0].Ref));
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        var conversation = f.ConversationService(host, execution: host);
        await conversation.SubmitCustomizationDiscovery(snapshot, shared.Eligible, Token);
        var workflow = shared.Workflow(conversation: conversation);
        using var review = await ReadSelectedSkill(workflow, snapshot.SkillProposal!, shared.Eligible);
        shared.Reader.Text += "\nchanged bytes";
        await workflow.Invoking(value => value.EnableAsync(review, shared.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.IsEnabled.Should().BeFalse();
        f.RetainCalls.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedSkillProposalCannotReadOrEnableUntilSelectorJoinAndCancellationCannotReleaseUseAuthority(bool cancelAtJoin)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        using var snapshot = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, shared.Eligible));
        var transport = new ToolTransport(CustomizationListCall, SkillSelectionAnswer(snapshot.Records[0].Ref));
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        var completions = 0;
        Activity? completionActivity = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var observe = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (cancelAtJoin && ReferenceEquals(activity, completionActivity)) { cancellation.Cancel(); }
            },
        };
        ActivitySource.AddActivityListener(observe);
        f.BeforeExecutionComplete = () =>
        {
            completions++;
            completionActivity = HostActivity.RequireCurrent().Activity;
            snapshot.SkillProposal.Should().NotBeNull();
            snapshot.SkillProposal!.IsCurrent.Should().BeFalse();
            ((Action)(() => snapshot.SkillProposal.RequireSnapshot(shared.Reader.Snapshot()))).Should().Throw<InvalidOperationException>();
            shared.Reader.ExactReads.Should().Be(0);
        };
        var receipt = await f.ConversationService(host, execution: host).SubmitCustomizationDiscovery(snapshot, shared.Eligible, cancellation.Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        completions.Should().Be(1);
        cancellation.IsCancellationRequested.Should().Be(cancelAtJoin);
        snapshot.SkillProposal!.IsCurrent.Should().Be(!cancelAtJoin);
        snapshot.SkillProposal.Description.Should().Contain("Original task").And.Contain("LocalOnly").And.Contain("32768").And.Contain("4096");
        snapshot.SkillProposal.Dispose();
        snapshot.SkillProposal.IsCurrent.Should().BeFalse();
        shared.Reader.ExactReads.Should().Be(0);
        shared.Audit.Events.Should().NotContain(item => item.ActionId.StartsWith("skills.revision.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectedSkillResolutionRepeatsCurrentnessAfterServedMetadataAndBeforeCreatingAnyProposal()
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var shared = new SharedSkillHarness(f);
        await shared.Register();
        var sources = SelectedSkillMetadata(shared);
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var metadata = new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time, shared.Discovery);
        using var snapshot = await ReviewSelectedMetadata(metadata, sources.Values[0].Source, new(f.Session, null), "Original task",
            new(RequestOrigin.LocalUi, shared.Eligible));
        snapshot.Consume();
        (await snapshot.Invoke(CustomizationDiscoveryTools.List, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
        snapshot.Retire();
        var result = snapshot.Resolve(new(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
            Response: new(JsonSerializer.Serialize(new { skillRef = snapshot.Records[0].Ref }), null)));
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        snapshot.SkillProposal.Should().BeNull();
        shared.Reader.ExactReads.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    private sealed class HeldSelectedSkillTransport(string reference, bool executor) : IModelToolTransport
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Joined { get; private set; }
        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            var held = executor ? request.Context.ToolMode == ModelToolMode.None : !request.Messages.IsEmpty;
            if (held)
            {
                using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
                Entered.TrySetResult();
                await Release.Task;
                Joined = true;
            }
            var wire = request.Context.ToolMode == ModelToolMode.None ? FinalAnswer
                : request.Messages.IsEmpty ? CustomizationListCall : SkillSelectionAnswer(reference);
            return ToolTransport.Reply(request, wire);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static MetadataSources SelectedSkillMetadata(SharedSkillHarness shared)
    {
        var sources = new MetadataSources();
        sources.Values = [sources.Values[0] with
        {
            Source = sources.Values[0].Source with { PrimaryFile = "SKILL.md" },
            Name = "concise-review",
            Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(shared.Reader.Text))),
        }];
        return sources;
    }

    private static string SkillSelectionAnswer(string reference) =>
        JsonSerializer.Serialize(new { kind = "answer", answer = JsonSerializer.Serialize(new { skillRef = reference }) });

    private static async Task<CustomizationDiscoverySnapshot> ReviewSelectedMetadata(CustomizationDiscoveryService metadata,
        CustomizationSource source, SessionWorkspaceEntry target, string task, OwnedSkillAuthoringAuthority original)
    {
        using var native = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        return await metadata.ReviewSkillSelection([source], target, task, original, original.IsCurrent, Token);
    }

    private static async Task<SharedSkillUseReview> ReadSelectedSkill(SharedSkillInvocationWorkflow workflow,
        ProposedSharedSkillUse proposal, Func<bool> eligible)
    {
        using var native = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        return await workflow.ReviewSelectedAsync(proposal, eligible, Token);
    }
}
