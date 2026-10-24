using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Skills;

[Collection("Host tracing")]
public sealed class SharedSkillReferenceInvocationTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public SharedSkillReferenceInvocationTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ExactOriginalChannelConsentPrecedesAllContentReadsAndCompleteReviewStillNeedsEnablement(RequestOrigin origin)
    {
        await using var f = new Fixture();
        await f.Register();
        var candidate = f.Reader.Snapshot();
        using var declaration = await f.Workflow.ReviewReferenceDeclarationAsync(f.Reader.Source, candidate, f.Eligible, origin, Token);
        declaration.DeclarationText.Should().Contain("notes.md").And.NotContain(f.Reader.Reference);
        f.Reader.ReferenceReads.Should().Be(0);
        await f.Workflow.Invoking(value => value.ReviewAsync(f.Reader.Source, candidate, f.Eligible, origin, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        using var review = await f.Workflow.ConsentToReferencesAsync(declaration, f.Eligible, origin, Token);
        f.Reader.ReferenceReads.Should().BeGreaterThan(0);
        review.Text.Should().Contain(candidate.Text!).And.Contain(f.Reader.Reference).And.Contain("REFERENCE notes.md");
        review.IsEnabled.Should().BeFalse();
        declaration.IsCurrent.Should().BeFalse();
        declaration.DeclarationText.Should().BeEmpty();
        await f.Workflow.Invoking(value => value.ConsentToReferencesAsync(declaration, f.Eligible, origin, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.Workflow.EnableAsync(review, f.Eligible, origin, Token);
        review.IsEnabled.Should().BeTrue();
        using var invocation = await f.Workflow.PrepareAsync(review, f.Target.Entry, "  Original unchanged prose.\r\n",
            f.Eligible, origin, Token);
        invocation.Body.Should().Be("  Original unchanged prose.\r\n");
        f.Reader.Reference += "\nChanged.";
        await f.Workflow.Invoking(value => value.EnableAsync(review, f.Eligible, origin, Token)).Should().ThrowAsync<InvalidOperationException>();
        review.IsCurrent.Should().BeFalse();
        f.Audit.Events.Where(item => string.Equals(item.ActionId, "skills.references.read", StringComparison.Ordinal)).Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.PreferenceStore.Writes.Should().Be(1);
    }

    [Fact]
    public async Task CompleteEnvelopeLimitRefusesOversizedBundleInsteadOfDroppingEvidence()
    {
        await using var f = new Fixture();
        f.Reader.Primary += "\n" + new string('x', 16 * 1024);
        f.Reader.Reference = new string('y', 16 * 1024);
        await f.Register();
        using var consent = await f.Workflow.ReviewReferenceDeclarationAsync(f.Reader.Source, f.Reader.Snapshot(),
            f.Eligible, RequestOrigin.LocalUi, Token);
        using var review = await f.Workflow.ConsentToReferencesAsync(consent, f.Eligible, RequestOrigin.LocalUi, Token);
        await f.Workflow.EnableAsync(review, f.Eligible, RequestOrigin.LocalUi, Token);
        await f.Workflow.Invoking(value => value.PrepareAsync(review, f.Target.Entry, "Original request.", f.Eligible,
            RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidDataException>();
        review.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task ForeignOwnerAndInvalidCandidatesCannotConsumeReferenceConsent()
    {
        await using var f = new Fixture();
        await using var other = new Fixture();
        await f.Register();
        await other.Register();
        var snapshot = f.Reader.Snapshot();
        using var consent = await f.Workflow.ReviewReferenceDeclarationAsync(f.Reader.Source, snapshot,
            f.Eligible, RequestOrigin.LocalUi, Token);
        await other.Workflow.Invoking(value => value.ConsentToReferencesAsync(consent, other.Eligible, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Reader.ReferenceReads.Should().Be(0);
        other.Reader.ReferenceReads.Should().Be(0);
        foreach (var candidate in new[]
        {
            snapshot.WithUnavailableReason("unsupported"),
            SharedSkillSnapshot.Parse(f.Reader.Source.Id, snapshot.RelativeFile, snapshot.Bytes, true, snapshot.UninspectedFiles),
        })
        {
            await f.Workflow.Invoking(value => value.ReviewReferenceDeclarationAsync(f.Reader.Source, candidate,
                f.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        await f.Workflow.Invoking(value => value.ReviewReferenceDeclarationAsync(other.Reader.Source, snapshot,
            f.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AdmissionChangeAtJoinedDeclarationTerminalCannotPublishReview()
    {
        await using var f = new Fixture();
        await f.Register();
        var terminals = 0;
        f.UseStore.BeforeCommit = record =>
        {
            if (record.IsTerminal && ++terminals == 2) { f.Open = false; }
        };
        await f.Workflow.Invoking(value => value.ReviewReferenceDeclarationAsync(f.Reader.Source, f.Reader.Snapshot(),
            f.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Reader.ReferenceReads.Should().Be(0);
    }

    [Fact]
    public async Task AdmissionChangeAtJoinedReadTerminalCannotPublishBundleReview()
    {
        await using var f = new Fixture();
        await f.Register();
        using var consent = await f.Workflow.ReviewReferenceDeclarationAsync(f.Reader.Source, f.Reader.Snapshot(),
            f.Eligible, RequestOrigin.LocalUi, Token);
        f.UseStore.BeforeCommit = record => { if (record.IsTerminal) { f.Open = false; } };
        await f.Workflow.Invoking(value => value.ConsentToReferencesAsync(consent,
            f.Eligible, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Reader.Joined.Should().BeTrue();
        consent.IsCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("declaration")]
    [InlineData("audit")]
    [InlineData("foreign")]
    public async Task RevokedOrChangedDeclarationsCannotReadOrEnable(string fault)
    {
        await using var f = new Fixture();
        await f.Register();
        using var declaration = await f.Workflow.ReviewReferenceDeclarationAsync(f.Reader.Source, f.Reader.Snapshot(),
            f.Eligible, RequestOrigin.LocalUi, Token);
        if (fault is "closed") { declaration.Dispose(); }
        if (fault is "expiry") { f.Time.Now = declaration.ExpiresAt; }
        if (fault is "clock") { f.Time.Now = f.Time.Now.AddSeconds(-1); }
        if (fault is "privacy") { f.Open = false; }
        if (fault is "control") { f.Access.ControlRevision++; }
        if (fault is "source") { f.Preferences.Save([]); }
        if (fault is "declaration") { f.Reader.Primary += "\nChanged declaration."; }
        if (fault is "audit") { f.Audit.Fail = SecurityAuditOutcome.Requested; }
        using var root = HostActivity.BeginRoot(HostRequest.Create(fault is "foreign" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var read = () => f.Workflow.ConsentToReferencesAsync(declaration, f.Eligible, RequestOrigin.LocalUi, Token);
        await read.Should().ThrowAsync<Exception>();
        f.Reader.ContentOpens.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "skills.revision.enable");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("withdraw")]
    [InlineData("dispose")]
    [InlineData("audit")]
    public async Task NonCooperativeReadRemainsOwnedUntilJoinedAndNeverReturnsStaleReview(string fault)
    {
        await using var f = new Fixture();
        await f.Register();
        using var declaration = await f.Workflow.ReviewReferenceDeclarationAsync(f.Reader.Source, f.Reader.Snapshot(),
            f.Eligible, RequestOrigin.LocalUi, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Reader.DuringRead = async () => { entered.SetResult(); await release.Task; };
        using var cancellation = new CancellationTokenSource();
        var pending = f.Workflow.ConsentToReferencesAsync(declaration, f.Eligible, RequestOrigin.LocalUi, cancellation.Token);
        await entered.Task;
        if (fault is "cancel") { await cancellation.CancelAsync(); }
        if (fault is "withdraw") { f.Preferences.Save([]); }
        if (fault is "dispose") { declaration.Dispose(); }
        if (fault is "audit") { f.Audit.Fail = SecurityAuditOutcome.Succeeded; }
        pending.IsCompleted.Should().BeFalse();
        release.SetResult();
        await ((Func<Task>)(async () => await pending)).Should().ThrowAsync<Exception>();
        f.Reader.Joined.Should().BeTrue();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "skills.revision.enable");
        declaration.IsCurrent.Should().BeFalse();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AudioControlTestStore nativeStore = new();
        private readonly AudioControlTestStore useStore = new();
        internal AudioControlTestStore UseStore => useStore;
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
            // These tests stop at review/enablement; native integration covers the unchanged conversation consumer.
            Workflow = new(Discovery, use, Access, Target, null!, null!, Audit, Time);
        }
        internal async Task Register()
        {
            await Discovery.LoadSourcesAsync(Eligible, Token);
            await Discovery.RegisterAsync("native selection", Eligible, Token);
        }
        public async ValueTask DisposeAsync() { await use.DisposeAsync(); await native.DisposeAsync(); }
    }

    private sealed class Reader : ISharedSkillSourceReader
    {
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Primary { get; set; } = "---\nname: example\nversion: 1.0.0\ndescription: Review\nformat: skill-md-instructions-v2\nreferences: notes.md\n---\nReview [notes](notes.md).";
        internal string Reference { get; set; } = "# Private notes\r\nExplain original prose.";
        internal int ReferenceReads { get; private set; }
        internal int ContentOpens { get; private set; }
        internal bool Joined { get; private set; }
        internal Func<Task>? DuringRead { get; set; }
        internal SharedSkillSnapshot Snapshot() => SharedSkillSnapshot.Parse(Source.Id, "example\\SKILL.md",
            Encoding.UTF8.GetBytes(Primary), true, ["example\\notes.md"]).WithPackageDirectoryIdentity(new string('b', 48));
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) => ValueTask.FromResult(Source);
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SharedSkillCatalogue(Source, [Snapshot()]));
        public async ValueTask<SharedSkillSnapshot> ReadReferencesAsync(SharedSkillSource source, SharedSkillSnapshot primary, CancellationToken cancellationToken)
        {
            ReferenceReads++;
            if (!primary.Bytes.SequenceEqual(Snapshot().Bytes)) { throw new InvalidOperationException("Primary changed before content open."); }
            ContentOpens++;
            if (DuringRead is { } during) { await during(); }
            Joined = true;
            var candidate = Snapshot();
            var result = candidate.WithReferenceBundle(SharedSkillReferenceBundle.Create(candidate,
                new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = Encoding.UTF8.GetBytes(Reference) }));
            if (primary.ReferenceBundle is not null && !string.Equals(primary.RevisionDigest, result.RevisionDigest, StringComparison.Ordinal))
            { throw new InvalidOperationException("Reference changed."); }
            return result;
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal SecurityAuditOutcome? Fail { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == Fail) { throw new IOException("Audit unavailable."); }
            Events.Add(auditEvent);
        }
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class TargetStore : AudioControlTestStore
    {
        internal SessionWorkspaceEntry Entry { get; } = new(new(new(Guid.NewGuid()), new(1), true), null);
        public override ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Entry);
    }
    private sealed class Access : ISessionWorkspaceAccess
    {
        public bool CanControl => true;
        public bool CanInspect => true;
        public long ControlRevision { get; set; }
    }
}
