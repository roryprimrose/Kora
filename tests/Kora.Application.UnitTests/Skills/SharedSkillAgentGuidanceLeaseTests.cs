using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Agents;
using Kora.Application.Configuration;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Skills;

[Collection("Host tracing")]
public sealed class SharedSkillAgentGuidanceLeaseTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public SharedSkillAgentGuidanceLeaseTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task SeparateReadAndEnableAreRequiredAndGuidanceConsumesAuthorityExactlyOnce(RequestOrigin origin)
    {
        await using var f = new Fixture();
        await f.Register();
        var candidate = f.Reader.Snapshot();
        using var declaration = await f.Workflow.ReviewReferenceDeclarationAsync(f.Reader.Source, candidate, f.Eligible, origin, Token);
        f.Reader.Reads.Should().Be(0);
        using var review = await f.Workflow.ConsentToReferencesAsync(declaration, f.Eligible, origin, Token);
        await f.Workflow.Invoking(value => value.AcquireForAgentAsync(review, f.Eligible, origin, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.IsCurrent.Should().BeFalse();
        using var enabled = await f.Capture(origin);
        await f.Workflow.EnableAsync(enabled, f.Eligible, origin, Token);
        using var lease = await f.Workflow.AcquireForAgentAsync(enabled, f.Eligible, origin, Token);
        lease.Snapshot.Bytes.ToArray().Should().Equal(candidate.Bytes.ToArray());
        lease.Snapshot.ReferenceBundle!.References.Single().Text.Should().Be(f.Reader.Reference);
        enabled.IsEnabled.Should().BeFalse();
        lease.IsCurrent.Should().BeTrue();
        await lease.ValidateAsync(Token);
        await f.Workflow.Invoking(value => value.PrepareAsync(enabled, f.Target.Entry, "Original", f.Eligible, origin, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.Workflow.Invoking(value => value.AcquireForAgentAsync(enabled, f.Eligible, origin, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        lease.IsCurrent.Should().BeFalse();
        f.PreferenceStore.Writes.Should().Be(1);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "skills.revision.invoke");
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("clock")]
    [InlineData("expiry")]
    [InlineData("registration")]
    [InlineData("package")]
    [InlineData("primary")]
    [InlineData("reference")]
    [InlineData("dispose")]
    public async Task ConsumedGuidanceRemainsFreshnessBoundAndNeverRetargets(string fault)
    {
        await using var f = new Fixture();
        await f.Register();
        using var review = await f.Capture();
        await f.Workflow.EnableAsync(review, f.Eligible, RequestOrigin.LocalUi, Token);
        using var lease = await f.Workflow.AcquireForAgentAsync(review, f.Eligible, RequestOrigin.LocalUi, Token);
        if (fault is "privacy") { f.Open = false; }
        if (fault is "control") { f.Access.ControlRevision++; }
        if (fault is "clock") { f.Time.Now = f.Time.Now.AddSeconds(-1); }
        if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
        if (fault is "registration") { f.Preferences.Save([]); }
        if (fault is "package") { f.Reader.Package = new string('c', 48); }
        if (fault is "primary") { f.Reader.Primary += "\nChanged"; }
        if (fault is "reference") { f.Reader.Reference += "\nChanged"; }
        if (fault is "dispose") { lease.Dispose(); }
        await lease.Invoking(value => value.ValidateAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        lease.IsCurrent.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "skills.revision.invoke");
    }

    [Fact]
    public async Task ResolverRequiresExplicitUniqueCurrentSourceSelectionBeforeAnyReferenceRead()
    {
        await using var f = new Fixture();
        await f.Register();
        var resolver = new AgentSkillReferenceResolver(f.Discovery, f.Workflow);
        (await resolver.ReadSourcesAsync(f.Eligible, Token)).Should().Equal(f.Reader.Source);
        using var review = resolver.Review(Profile(f.Reader.Snapshot().SourceQualifiedIdentity));
        await review.DeclareAsync(f.Reader.Source, f.Reader.Snapshot(), f.Eligible, RequestOrigin.LocalUi, Token);
        f.Reader.Reads.Should().Be(0);
        review.DeclarationText.Should().Contain("notes.md").And.NotContain(f.Reader.Reference);
        await review.ConsentAsync(f.Eligible, RequestOrigin.LocalUi, Token);
        review.HasBundle.Should().BeTrue();
        review.Snapshot.ReferenceBundle.Should().NotBeNull();
        review.CompleteText.Should().Contain(f.Reader.Reference);
        await review.EnableAsync(f.Eligible, RequestOrigin.LocalUi, Token);
        review.IsEnabled.Should().BeTrue();
        await review.AcquireAsync(f.Eligible, RequestOrigin.LocalUi, Token);
        review.Lease!.IsCurrent.Should().BeTrue();
        await review.Invoking(value => value.AcquireAsync(f.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("changed")]
    [InlineData("package")]
    [InlineData("source")]
    [InlineData("identity")]
    [InlineData("name")]
    public async Task ResolverCannotSubstituteOrDisambiguateSelectedCandidate(string fault)
    {
        await using var f = new Fixture();
        await f.Register();
        var resolver = new AgentSkillReferenceResolver(f.Discovery, f.Workflow);
        var selected = f.Reader.Snapshot();
        using var review = resolver.Review(Profile(fault switch
        {
            "identity" => $"shared-profile:{Guid.NewGuid():N}:example@1.0.0",
            "name" => $"shared-profile:{f.Reader.Source.Id:N}:other@1.0.0",
            _ => selected.SourceQualifiedIdentity,
        }));
        if (fault is "duplicate") { f.Reader.Duplicate = true; }
        if (fault is "changed") { f.Reader.Primary += "\nChanged"; }
        if (fault is "package") { f.Reader.Package = new string('c', 48); }
        var source = fault is "source" ? new SharedSkillSource(Guid.NewGuid(), ".other\\skills", new string('d', 48)) : f.Reader.Source;
        await review.Invoking(value => value.DeclareAsync(source, selected, f.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Reader.Reads.Should().Be(0);
    }

    [Fact]
    public async Task ForeignWorkflowCannotCaptureEnabledReviewAndCancellationIgnoringReadIsJoined()
    {
        await using var f = new Fixture();
        await using var other = new Fixture();
        await f.Register();
        await other.Register();
        using var review = await f.Capture();
        await f.Workflow.EnableAsync(review, f.Eligible, RequestOrigin.LocalUi, Token);
        await other.Workflow.Invoking(value => value.AcquireForAgentAsync(review, other.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        other.Reader.Reads.Should().Be(0);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Reader.Pending = gate.Task;
        using var consent = await f.Workflow.ReviewReferenceDeclarationAsync(f.Reader.Source, f.Reader.Snapshot(),
            f.Eligible, RequestOrigin.LocalUi, Token);
        using var cancellation = new CancellationTokenSource();
        var pending = f.Workflow.ConsentToReferencesAsync(consent, f.Eligible, RequestOrigin.LocalUi, cancellation.Token);
        await cancellation.CancelAsync();
        consent.Dispose();
        pending.IsCompleted.Should().BeFalse();
        gate.SetResult();
        await pending.Invoking(value => value).Should().ThrowAsync<OperationCanceledException>();
        f.Reader.Joined.Should().BeTrue();
    }

    [Fact]
    public async Task AgentGuidanceStageFailuresCannotSkipConsentEnableOrRetirement()
    {
        await using var f = new Fixture();
        await f.Register();
        var resolver = new AgentSkillReferenceResolver(f.Discovery, f.Workflow);
        using var stage = resolver.Review(Profile(f.Reader.Snapshot().SourceQualifiedIdentity));
        stage.DeclarationText.Should().BeEmpty();
        stage.CompleteText.Should().BeEmpty();
        stage.IsEnabled.Should().BeFalse();
        stage.HasBundle.Should().BeFalse();
        var source = () => stage.Snapshot;
        source.Should().Throw<InvalidOperationException>();
        await stage.Invoking(value => value.ConsentAsync(f.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        await stage.Invoking(value => value.EnableAsync(f.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        await stage.Invoking(value => value.AcquireAsync(f.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        await stage.DeclareAsync(f.Reader.Source, f.Reader.Snapshot(), f.Eligible, RequestOrigin.LocalUi, Token);
        await stage.Invoking(value => value.DeclareAsync(f.Reader.Source, f.Reader.Snapshot(), f.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await stage.ConsentAsync(f.Eligible, RequestOrigin.LocalUi, Token);
        stage.DeclarationText.Should().BeEmpty();
        await stage.Invoking(value => value.DeclareAsync(f.Reader.Source, f.Reader.Snapshot(), f.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await stage.EnableAsync(f.Eligible, RequestOrigin.LocalUi, Token);
        await stage.AcquireAsync(f.Eligible, RequestOrigin.LocalUi, Token);
        stage.IsCurrent.Should().BeTrue();
        stage.Lease!.Dispose();
        stage.IsCurrent.Should().BeFalse();
        stage.Dispose();
        stage.IsCurrent.Should().BeFalse();
        await stage.Invoking(value => value.DeclareAsync(f.Reader.Source, f.Reader.Snapshot(), f.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var legacy = BoundedAgentProfile.Parse(ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            Profile(f.Reader.Snapshot().SourceQualifiedIdentity).Document.Text
                .Replace("kora-agent-format: 3", "kora-agent-format: 1", StringComparison.Ordinal)
                .Replace(f.Reader.Snapshot().SourceQualifiedIdentity, "none", StringComparison.Ordinal), false),
            ArtifactKind.Agent, "selected-user-source", "example-agent"));
        var unsupported = () => resolver.Review(legacy);
        unsupported.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("declaration")]
    [InlineData("review")]
    public async Task AgentGuidanceIsCurrentTracksPendingStageExpiry(string stage)
    {
        await using var f = new Fixture();
        await f.Register();
        var resolver = new AgentSkillReferenceResolver(f.Discovery, f.Workflow);
        using var review = resolver.Review(Profile(f.Reader.Snapshot().SourceQualifiedIdentity));
        await review.DeclareAsync(f.Reader.Source, f.Reader.Snapshot(), f.Eligible, RequestOrigin.LocalUi, Token);
        if (stage is "review") { await review.ConsentAsync(f.Eligible, RequestOrigin.LocalUi, Token); }
        f.Time.Now = f.Time.Now.AddMinutes(3);
        review.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task AgentGuidanceLeaseRechecksRevocationAfterAwaitedValidation()
    {
        await using var f = new Fixture();
        await f.Register();
        using var review = await f.Capture();
        using var lease = new AgentSkillGuidanceLease(f.Workflow.RequireAgentSnapshot(review), review, _ =>
        {
            review.Dispose();
            return Task.CompletedTask;
        });
        await lease.Invoking(value => value.ValidateAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        lease.IsCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("package")]
    [InlineData("unavailable")]
    public async Task AgentGuidanceRejectsUnpinnedOrForeignSnapshotsBeforeReferenceRead(string fault)
    {
        await using var f = new Fixture();
        await f.Register();
        var resolver = new AgentSkillReferenceResolver(f.Discovery, f.Workflow);
        var snapshot = f.Reader.Snapshot();
        using var stage = resolver.Review(Profile(snapshot.SourceQualifiedIdentity));
        var selected = fault switch
        {
            "source" => SharedSkillSnapshot.Parse(Guid.NewGuid(), snapshot.RelativeFile, snapshot.Bytes, true, snapshot.UninspectedFiles),
            "package" => SharedSkillSnapshot.Parse(snapshot.SourceId, snapshot.RelativeFile, snapshot.Bytes, true, snapshot.UninspectedFiles),
            _ => snapshot.WithUnavailableReason("unavailable"),
        };
        await stage.Invoking(value => value.DeclareAsync(f.Reader.Source, selected, f.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Reader.Reads.Should().Be(0);
    }

    internal static BoundedAgentProfile Profile(string identity) =>
        BoundedAgentProfile.Parse(ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"""
            ---
            name: example-agent
            description: Local guidance
            kora-agent-format: 3
            runtime: local
            input: text
            output: text
            references: {identity}
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Explain only the original request.
            """, false), ArtifactKind.Agent, "selected-user-source", "example-agent"));

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly AudioControlTestStore nativeStore = new();
        private readonly AudioControlTestStore useStore = new();
        private readonly SharedSkillAdmission native;
        private readonly SharedSkillUseAdmission use;
        internal LocalSharedSkillPreferencesTests.Store PreferenceStore { get; } = new();
        internal LocalSharedSkillPreferences Preferences { get; }
        internal Reader Reader { get; } = new();
        internal Audit Audit { get; } = new();
        internal Clock Time { get; } = new();
        internal Access Access { get; } = new();
        internal TargetStore Target { get; } = new();
        internal bool Open { get; set; } = true;
        internal bool Eligible() => Open;
        internal SharedSkillDiscoveryService Discovery { get; }
        internal SharedSkillInvocationWorkflow Workflow { get; }
        internal Fixture()
        {
            Preferences = new(PreferenceStore);
            native = new(nativeStore, nativeStore, new(nativeStore));
            use = new(useStore, useStore, new(useStore));
            Discovery = new(Preferences, Reader, native, Audit, NullLogger<SharedSkillDiscoveryService>.Instance);
            Workflow = new(Discovery, use, Access, Target, null!, null!, Audit, Time);
        }
        internal async Task Register()
        {
            await Discovery.LoadSourcesAsync(Eligible, Token);
            await Discovery.RegisterAsync("native selection", Eligible, Token);
        }
        internal async Task<SharedSkillUseReview> Capture(RequestOrigin origin = RequestOrigin.LocalUi)
        {
            using var declaration = await Workflow.ReviewReferenceDeclarationAsync(Reader.Source, Reader.Snapshot(), Eligible, origin, Token);
            return await Workflow.ConsentToReferencesAsync(declaration, Eligible, origin, Token);
        }
        public async ValueTask DisposeAsync() { await use.DisposeAsync(); await native.DisposeAsync(); }
    }

    internal sealed class Reader : ISharedSkillSourceReader
    {
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Primary { get; set; } = "---\nname: example\nversion: 1.0.0\ndescription: Review\nformat: skill-md-instructions-v2\nreferences: notes.md\n---\nReview [notes](notes.md).";
        internal string Reference { get; set; } = "# Private notes\r\nExplain original prose.";
        internal string Package { get; set; } = new('b', 48);
        internal bool Duplicate { get; set; }
        internal int Reads { get; private set; }
        internal bool Joined { get; private set; }
        internal Task? Pending { get; set; }
        internal Action? OnRead { get; set; }
        internal SharedSkillSnapshot Snapshot(string path = "example\\SKILL.md") => SharedSkillSnapshot.Parse(Source.Id, path,
            Encoding.UTF8.GetBytes(Primary), true, [path.Replace("SKILL.md", "notes.md", StringComparison.Ordinal)]).WithPackageDirectoryIdentity(Package);
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) => ValueTask.FromResult(Source);
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SharedSkillCatalogue(Source, Duplicate ? [Snapshot(), Snapshot("other\\SKILL.md")] : [Snapshot()]));
        public async ValueTask<SharedSkillSnapshot> ReadReferencesAsync(SharedSkillSource source, SharedSkillSnapshot primary, CancellationToken cancellationToken)
        {
            Reads++;
            OnRead?.Invoke();
            if (Pending is { } pending) { await pending; }
            Joined = true;
            var candidate = Snapshot();
            var result = candidate.WithReferenceBundle(SharedSkillReferenceBundle.Create(candidate,
                new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = Encoding.UTF8.GetBytes(Reference) }));
            return result;
        }
    }
    internal sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class TargetStore : AudioControlTestStore
    {
        internal SessionWorkspaceEntry Entry { get; } = new(new(new(Guid.NewGuid()), new(1), true), null);
        public override ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Entry);
    }
    internal sealed class Access : ISessionWorkspaceAccess
    {
        public bool CanControl => true;
        public bool CanInspect => true;
        public long ControlRevision { get; set; }
    }
}
