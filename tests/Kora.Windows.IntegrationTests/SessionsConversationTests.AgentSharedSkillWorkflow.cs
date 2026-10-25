using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Agents;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Skills;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task SharedSkillAgentRequiresDistinctConsentsThenRetainsOneGenuineLocalAnswer(RequestOrigin origin)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var f = new SharedAgentFixture(storage);
        f.Composed.Audit.ExpectedOrigin = origin;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        using var review = await f.Review(origin);
        review.IsInvocationReady.Should().BeFalse();
        f.Reader.Reads.Should().Be(0);
        await f.Workflow.Invoking(value => value.InvokeAsync(review, origin, storage.Token)).Should().ThrowAsync<InvalidOperationException>();
        await f.Workflow.ReviewSkillDeclarationAsync(review, f.Reader.Source, f.Reader.Snapshot(), origin, storage.Token);
        f.Reader.Reads.Should().Be(0);
        review.Skill!.DeclarationText.Should().Contain("notes.md").And.NotContain(f.Reader.Reference);
        await f.Workflow.ConsentToSkillReferencesAsync(review, origin, storage.Token);
        review.Skill.CompleteText.Should().Contain(f.Reader.Primary).And.Contain(f.Reader.Reference);
        review.IsInvocationReady.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        storage.Count("conversation_messages").Should().Be(0);
        await f.Workflow.EnableSkillAsync(review, origin, storage.Token);
        review.IsInvocationReady.Should().BeTrue();
        await f.Workflow.Invoking(value => value.InvokeAsync(review, storage.Token)).Should().ThrowAsync<InvalidOperationException>();
        var receipt = await f.Workflow.InvokeAsync(review, origin, storage.Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Calls.Should().Be(1);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().ToArray();
        evidence.Should().HaveCount(3);
        evidence.Should().OnlyContain(item => item.GetProperty("Disclosure").GetInt32() == (int)ModelEvidenceDisclosure.LocalOnly);
        var serialized = Encoding.UTF8.GetString(f.Adapter.Wire!);
        serialized.Should().Contain("Exact role evidence.").And.Contain("Exact primary evidence.")
            .And.Contain("Exact declared reference evidence.");
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("Constraints").GetProperty("MaximumCalls").GetInt32().Should().Be(0);
        var message = (await storage.Store.ReadConversationPageAsync(f.Target.Authority.SessionId, null, 25, storage.Token)).Messages.Single();
        message.Body.Should().Be("  Exact original request.\r\n");
        message.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
        await f.Workflow.Invoking(value => value.InvokeAsync(review, origin, storage.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Reader.Reads.Should().BeGreaterThan(1);
        f.Profile.Released.Should().BeTrue();
        f.Composed.Audit.Events.Where(item => string.Equals(item.ActionId, "agents.revision.invoke", StringComparison.Ordinal))
            .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Composed.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "skills.revision.invoke", StringComparison.Ordinal));
        storage.Count("scoped_grants").Should().Be(0);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("primary")]
    [InlineData("reference")]
    [InlineData("package")]
    [InlineData("registration")]
    [InlineData("privacy")]
    [InlineData("input")]
    [InlineData("expiry")]
    public async Task SharedSkillAgentChangesFailBeforeModelAdmissionWithoutReplacement(string change)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var f = new SharedAgentFixture(storage);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var review = await f.Enabled();
        if (change is "profile") { f.Profile.Text += "\nChanged"; }
        if (change is "primary") { f.Reader.Primary += "\nChanged"; }
        if (change is "reference") { f.Reader.Reference += "\nChanged"; }
        if (change is "package") { f.Reader.Package = new string('c', 48); }
        if (change is "registration") { f.Preferences.Save([]); }
        if (change is "privacy") { f.Composed.Access.CanControl = false; }
        if (change is "input") { f.Open = false; }
        if (change is "expiry") { storage.Time.Now = review.ExpiresAt; }
        await f.Workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, storage.Token)).Should().ThrowAsync<Exception>();
        f.Adapter.Calls.Should().Be(0);
        storage.Count("conversation_messages").Should().Be(0);
        await f.Workflow.RevokeAsync(review);
        f.Profile.Released.Should().BeTrue();
    }

    [Fact]
    public async Task SharedSkillAgentWithoutResolverIsExplicitlyUnavailableAndDoesNotReadSkills()
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var f = new SharedAgentFixture(storage);
        await using var workflow = new BoundedAgentWorkflow(f.Profile, storage.Store, f.Composed.Access,
            f.Composed.Conversation, f.Composed.Turns, f.Composed.Audit, storage.Time);
        await workflow.Invoking(value => value.ReviewAsync("selected.agent.md", f.Target, "Original",
            RequestOrigin.LocalUi, () => true, storage.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Reader.Reads.Should().Be(0);
        f.Profile.Released.Should().BeTrue();
    }

    [Fact]
    public async Task SharedSkillAgentRevokeJoinsCancellationIgnoringReferenceReadBeforeReleasingProfile()
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var f = new SharedAgentFixture(storage);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var review = await f.Review();
        await f.Workflow.ReviewSkillDeclarationAsync(review, f.Reader.Source, f.Reader.Snapshot(), RequestOrigin.LocalUi, storage.Token);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Reader.Pending = gate.Task;
        var reading = f.Workflow.ConsentToSkillReferencesAsync(review, RequestOrigin.LocalUi, storage.Token);
        await f.Reader.Started.Task;
        var stopping = f.Workflow.RevokeAsync(review);
        reading.IsCompleted.Should().BeFalse();
        stopping.IsCompleted.Should().BeFalse();
        f.Profile.Released.Should().BeFalse();
        gate.SetResult();
        await reading.Invoking(value => value).Should().ThrowAsync<OperationCanceledException>();
        await stopping;
        f.Profile.Released.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
        storage.Count("conversation_messages").Should().Be(0);
    }

    [Fact]
    public async Task SharedSkillAgentRevokeJoinsIgnoringModelAndRejectsItsLateAnswer()
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        var adapter = new IgnoringAgentAdapter();
        await using var f = new SharedAgentFixture(storage, adapter);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var review = await f.Enabled();
        var running = f.Workflow.InvokeAsync(review, RequestOrigin.LocalUi, storage.Token);
        await adapter.Started.Task;
        var stopping = f.Workflow.RevokeAsync(review);
        running.IsCompleted.Should().BeFalse();
        stopping.IsCompleted.Should().BeFalse();
        f.Profile.Released.Should().BeFalse();
        adapter.Reply.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("unauthorized late answer", null)));
        await running.Invoking(value => value).Should().ThrowAsync<Exception>();
        await stopping;
        f.Profile.Released.Should().BeTrue();
        f.Composed.Turns.IsQuiescent.Should().BeTrue();
        var message = (await storage.Store.ReadConversationPageAsync(f.Target.Authority.SessionId, null, 25, storage.Token)).Messages.Single();
        message.AssistantResponse.Should().BeNull();
        message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSkillAgentRejectsCompleteSerializedOrAggregateSourceOverflowWithoutDroppingFiles(bool sourceOverflow)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var f = new SharedAgentFixture(storage);
        if (sourceOverflow)
        {
            f.Profile.Text += "\n" + new string('p', 40 * 1024);
            f.Reader.Primary += "\n" + new string('s', 24 * 1024);
        }
        else
        {
            f.Reader.Primary += "\n" + new string('s', 16 * 1024);
            f.Reader.Reference = new string('r', 16 * 1024);
        }
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var review = await f.Review();
        await f.Workflow.ReviewSkillDeclarationAsync(review, f.Reader.Source, f.Reader.Snapshot(), RequestOrigin.LocalUi, storage.Token);
        await f.Workflow.Invoking(value => value.ConsentToSkillReferencesAsync(review, RequestOrigin.LocalUi, storage.Token))
            .Should().ThrowAsync<InvalidDataException>();
        review.IsInvocationReady.Should().BeFalse();
        f.Profile.Released.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
        storage.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SharedSkillAgentEnforcesExactCompleteSerializedInputThresholdIncludingMetadata(int overflow)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        int baseline;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await using (var measured = new SharedAgentFixture(storage))
        {
            using var review = await measured.Enabled();
            await measured.Workflow.InvokeAsync(review, RequestOrigin.LocalUi, storage.Token);
            baseline = measured.Adapter.Wire!.Length;
        }
        await using var f = new SharedAgentFixture(storage);
        f.Reader.Primary += new string('s', ModelContextEnvelope.MaximumInputUtf8Bytes - baseline + overflow);
        using var pending = await f.Review();
        await f.Workflow.ReviewSkillDeclarationAsync(pending, f.Reader.Source, f.Reader.Snapshot(), RequestOrigin.LocalUi, storage.Token);
        if (overflow == 1)
        {
            await f.Workflow.Invoking(value => value.ConsentToSkillReferencesAsync(pending, RequestOrigin.LocalUi, storage.Token))
                .Should().ThrowAsync<InvalidDataException>();
            f.Adapter.Calls.Should().Be(0);
        }
        else
        {
            await f.Workflow.ConsentToSkillReferencesAsync(pending, RequestOrigin.LocalUi, storage.Token);
            await f.Workflow.EnableSkillAsync(pending, RequestOrigin.LocalUi, storage.Token);
            await f.Workflow.InvokeAsync(pending, RequestOrigin.LocalUi, storage.Token);
            f.Adapter.Wire!.Length.Should().Be(ModelContextEnvelope.MaximumInputUtf8Bytes);
        }
    }

    private sealed class SharedAgentFixture : IAsyncDisposable
    {
        private readonly InteractionStorageFixture storage;
        private readonly SharedSkillAdmission native;
        private readonly SharedSkillUseAdmission use;
        internal Composition Composed { get; }
        internal HistoryAdapter Adapter { get; } = new();
        internal NativeAgentReader Profile { get; } = new();
        internal AgentSharedReader Reader { get; } = new();
        internal LocalSharedSkillPreferences Preferences { get; }
        internal BoundedAgentWorkflow Workflow { get; }
        internal SessionWorkspaceEntry Target { get; }
        internal bool Open { get; set; } = true;

        internal SharedAgentFixture(InteractionStorageFixture storage, IModelTurnAdapter? adapter = null)
        {
            this.storage = storage;
            Target = new(new(storage.Request.SessionId, new(1), true), null);
            Composed = new(storage, adapter: adapter ?? Adapter);
            Preferences = new(storage.Paths);
            Preferences.Save([Reader.Source]);
            native = new(storage.Store, storage.Store, new(storage.Tasks));
            use = new(storage.Store, storage.Store, new(storage.Tasks));
            var discovery = new SharedSkillDiscoveryService(Preferences, Reader, native, Composed.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            var shared = new SharedSkillInvocationWorkflow(discovery, use, Composed.Access, storage.Store,
                Composed.Workspace, Composed.Conversation, Composed.Audit, storage.Time);
            Profile.Text = Profile.Text.Replace("kora-agent-format: 1", "kora-agent-format: 3", StringComparison.Ordinal)
                .Replace("references: none", "references: " + Reader.Snapshot().SourceQualifiedIdentity, StringComparison.Ordinal)
                .Replace("Explain the original request. No scripts or imported references.", "Exact role evidence.", StringComparison.Ordinal);
            Workflow = new(Profile, storage.Store, Composed.Access, Composed.Conversation, Composed.Turns,
                Composed.Audit, storage.Time, skills: new(discovery, shared));
        }

        internal async Task<BoundedAgentReview> Review(RequestOrigin origin = RequestOrigin.LocalUi) =>
            await Workflow.ReviewAsync("selected.agent.md",
                (await storage.Store.ReadMetadataAsync(Target.Authority.SessionId, storage.Token))!,
                "  Exact original request.\r\n", origin, () => Open, storage.Token);

        internal async Task<BoundedAgentReview> Enabled()
        {
            var review = await Review();
            await Workflow.ReviewSkillDeclarationAsync(review, Reader.Source, Reader.Snapshot(), RequestOrigin.LocalUi, storage.Token);
            await Workflow.ConsentToSkillReferencesAsync(review, RequestOrigin.LocalUi, storage.Token);
            await Workflow.EnableSkillAsync(review, RequestOrigin.LocalUi, storage.Token);
            return review;
        }
        public async ValueTask DisposeAsync()
        {
            await Workflow.DisposeAsync();
            await use.DisposeAsync();
            await native.DisposeAsync();
            await Composed.DisposeAsync();
        }
    }

    private sealed class AgentSharedReader : ISharedSkillSourceReader
    {
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Primary { get; set; } = "---\nname: example\nversion: 1.0.0\ndescription: Local guidance\nformat: skill-md-instructions-v2\nreferences: notes.md\n---\nExact primary evidence. [Notes](notes.md).";
        internal string Reference { get; set; } = "# Notes\r\nExact declared reference evidence.";
        internal string Package { get; set; } = new('b', 48);
        internal int Reads { get; private set; }
        internal Task? Pending { get; set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SharedSkillSnapshot Snapshot() => SharedSkillSnapshot.Parse(Source.Id, "example\\SKILL.md",
            Encoding.UTF8.GetBytes(Primary), true, ["example\\notes.md"]).WithPackageDirectoryIdentity(Package);
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No registration authority.");
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SharedSkillCatalogue(Source, [Snapshot()]));
        public async ValueTask<SharedSkillSnapshot> ReadReferencesAsync(SharedSkillSource source, SharedSkillSnapshot primary, CancellationToken cancellationToken)
        {
            Reads++;
            Started.TrySetResult();
            if (Pending is { } pending) { await pending; }
            var candidate = Snapshot();
            return candidate.WithReferenceBundle(SharedSkillReferenceBundle.Create(candidate,
                new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = Encoding.UTF8.GetBytes(Reference) }));
        }
    }
}
