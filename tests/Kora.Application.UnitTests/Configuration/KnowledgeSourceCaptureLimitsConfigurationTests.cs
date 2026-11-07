using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class KnowledgeSourceCaptureLimitsConfigurationTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public KnowledgeSourceCaptureLimitsConfigurationTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly KnowledgeSourceCaptureLimits Lowered = new(2, 100, 200);

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ReadReviewConfirmResetAndRestartAffectOnlyExactSourceNumericPreference(RequestOrigin origin)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, origin);
        var f = new Fixture();
        var other = Guid.NewGuid();
        f.Preferences.Save(other, new(1, 1, 1));
        var initial = await f.Service.GetAsync("TEAM", () => true, Token);
        initial.Saved.Should().BeNull();
        var review = await f.Service.ReviewAsync("Team", Lowered, () => true, Token);
        review.Source.Id.Should().Be(f.Source.Id);
        review.Saved.Should().BeNull();
        review.Proposed.Should().Be(Lowered);
        f.Events.Should().BeEmpty();
        f.Preferences.Load(f.Source.Id).Should().BeNull();
        await f.Service.ConfirmAsync(review, () => true, Token);
        f.Preferences.Load(f.Source.Id).Should().Be(Lowered);
        f.Preferences.Load(other).Should().Be(new KnowledgeSourceCaptureLimits(1, 1, 1));
        f.Events.Select(e => e.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Events[0].Initiator.Should().Be(origin == RequestOrigin.ActivatedVoice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser);
        f.Source.Folder.CaptureLimits.Should().Be(KnowledgeSourceCaptureLimits.HardMaximum);
        f.BodyReads.Should().Be(0);
        f.SourceWrites.Should().Be(0);
        await FluentActions.Awaiting(() => f.Service.ConfirmAsync(review, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var restarted = f.NewService();
        (await restarted.GetAsync("Team", () => true, Token)).Saved.Should().Be(Lowered);
        var reset = await restarted.ReviewAsync("Team", null, () => true, Token);
        await restarted.ConfirmAsync(reset, () => true, Token);
        new LocalKnowledgeSourceCaptureLimitsPreferences(f.Files).Load(f.Source.Id).Should().BeNull();
        f.Preferences.Load(other).Should().NotBeNull();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expired")]
    [InlineData("source")]
    [InlineData("metadata")]
    [InlineData("preference")]
    [InlineData("revoked")]
    [InlineData("replaced-review")]
    [InlineData("audit-source")]
    [InlineData("audit-preference")]
    [InlineData("audit-expired")]
    public async Task StaleSourcePreferenceReviewAndAdmissionCannotWrite(string change)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var f = new Fixture();
        var eligible = true;
        var review = await f.Service.ReviewAsync("Team", Lowered, () => eligible, Token);
        switch (change)
        {
            case "cancel": f.Service.Cancel(); break;
            case "expired": f.Time.Now += TimeSpan.FromMinutes(2); break;
            case "source": f.Source = f.Source with { Version = 2 }; break;
            case "metadata": f.Source = f.Source with { Folder = Metadata("other-root") }; break;
            case "preference": f.Preferences.Save(f.Source.Id, new(1, 1, 1)); break;
            case "revoked": eligible = false; break;
            case "replaced-review": _ = await f.Service.ReviewAsync("Team", null, () => true, Token); break;
            case "audit-source": f.AfterAudit = () => f.Source = f.Source with { Version = 2 }; break;
            case "audit-preference": f.AfterAudit = () => f.Preferences.Save(f.Source.Id, new(1, 1, 1)); break;
            case "audit-expired": f.AfterAudit = () => f.Time.Now += TimeSpan.FromMinutes(2); break;
        }
        var writes = f.Files.Writes;
        await FluentActions.Awaiting(() => f.Service.ConfirmAsync(review, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(writes + (change is "audit-preference" ? 2 : 0));
        f.Files.Values.Keys.Should().NotContain(key => key.EndsWith("-unconfirmed.txt", StringComparison.Ordinal));
        f.Events.Should().NotContain(e => e.Outcome == SecurityAuditOutcome.Succeeded);
        f.BodyReads.Should().Be(0);
        f.SourceWrites.Should().Be(0);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("ambiguous")]
    [InlineData("removing")]
    public async Task UnknownAmbiguousAndRemovingSourcesHaveNoPreferenceFallback(string scenario)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var f = new Fixture { Duplicate = string.Equals(scenario, "ambiguous", StringComparison.Ordinal) };
        if (string.Equals(scenario, "removing", StringComparison.Ordinal)) { f.Source = f.Source with { State = KnowledgeSourceState.Removing }; }
        await FluentActions.Awaiting(() => f.Service.GetAsync(
            string.Equals(scenario, "unknown", StringComparison.Ordinal) ? "other" : "Team", () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nname")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task InvalidNamesCannotReadSourceInventory(string name)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var f = new Fixture();
        await FluentActions.Awaiting(() => f.Service.GetAsync(name, () => true, Token)).Should().ThrowAsync<InvalidDataException>();
        f.Lists.Should().Be(0);
    }

    [Theory]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("confirmed-readback")]
    [InlineData("cancel-after-save")]
    [InlineData("revoke-after-save")]
    public async Task FailedWritesAndAuditCannotAdvertiseConfirmedRefreshLimits(string scenario)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var f = new Fixture();
        var eligible = true;
        var review = await f.Service.ReviewAsync("Team", Lowered, () => eligible, Token);
        if (string.Equals(scenario, "requested-audit", StringComparison.Ordinal)) { f.ThrowAuditAt = 1; }
        if (string.Equals(scenario, "terminal-audit", StringComparison.Ordinal)) { f.ThrowAuditAt = 2; }
        if (string.Equals(scenario, "save", StringComparison.Ordinal)) { f.Files.ThrowWriteAt = 2; }
        if (string.Equals(scenario, "readback", StringComparison.Ordinal))
        { f.Files.AfterWrite = () => { if (f.Files.Writes == 2) { f.Files.Values.Clear(); } }; }
        if (string.Equals(scenario, "confirmed-readback", StringComparison.Ordinal)) { f.Files.AfterDelete = () => f.Files.Values.Clear(); }
        if (string.Equals(scenario, "cancel-after-save", StringComparison.Ordinal)) { f.Files.AfterWrite = cancellation.Cancel; }
        if (string.Equals(scenario, "revoke-after-save", StringComparison.Ordinal)) { f.Files.AfterWrite = () => eligible = false; }
        var action = () => f.Service.ConfirmAsync(review, () => eligible, cancellation.Token);
        if (scenario is "cancel-after-save") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else if (scenario is "revoke-after-save") { await action.Should().ThrowAsync<InvalidOperationException>(); }
        else if (scenario is "readback" or "confirmed-readback") { await action.Should().ThrowAsync<InvalidDataException>(); }
        else { await action.Should().ThrowAsync<IOException>(); }
        if (scenario is "requested-audit") { f.Preferences.Load(f.Source.Id).Should().BeNull(); }
        else { f.Preferences.Invoking(p => p.Load(f.Source.Id)).Should().Throw<InvalidDataException>(); }
        f.SourceWrites.Should().Be(0);
    }

    [Theory]
    [InlineData("not-a-tuple")]
    [InlineData("1:0:1:1")]
    [InlineData("1:1:1:1048577")]
    public async Task MalformedSavedStateCannotBecomeDefaultOrResetWrite(string text)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var f = new Fixture();
        f.Files.Values["knowledge-capture-" + f.Source.Id.ToString("N") + ".txt"] = text;
        await FluentActions.Awaiting(() => f.Service.ReviewAsync("Team", null, () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CancellationIgnoringInventoryRetainsOwnershipAndRejectsLateReview()
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var f = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ListWait = release.Task;
        var eligible = true;
        var pending = f.Service.ReviewAsync("Team", Lowered, () => eligible, Token);
        f.Service.Invoking(s => s.Cancel()).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => f.Service.GetAsync("Team", () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        eligible = false;
        release.SetResult();
        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<InvalidOperationException>();
        f.Service.Cancel();
        f.Files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("terminal")]
    [InlineData("eligibility")]
    [InlineData("cancelled")]
    public async Task ForeignOrRetiredAuthorityCannotEvenInspectPreferences(string scenario)
    {
        var request = HostRequest.Create(scenario is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (scenario is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (scenario is "terminal") { host.Complete(HostOperationOutcome.Completed); }
        var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (scenario is "cancelled") { cancellation.Cancel(); }
        var action = () => f.Service.GetAsync("Team", () => scenario is not "eligibility", cancellation.Token);
        if (scenario is "cancelled") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        f.Lists.Should().Be(0);
    }

    [Fact]
    public async Task ReviewExpiringDuringSourceResolutionCannotRequestAuditOrWrite()
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var f = new Fixture();
        var review = await f.Service.ReviewAsync("Team", Lowered, () => true, Token);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ListWait = release.Task;
        var pending = f.Service.ConfirmAsync(review, () => true, Token);
        f.Lists.Should().Be(2);
        f.Time.Now += TimeSpan.FromMinutes(2);
        release.SetResult();
        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<InvalidOperationException>();
        f.Events.Should().BeEmpty();
        f.Files.Writes.Should().Be(0);
        f.BodyReads.Should().Be(0);
        f.SourceWrites.Should().Be(0);
    }

    [Fact]
    public async Task UnreadableLimitsLogOnlyContentFreeUnavailableDiagnosticWithoutWriting()
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var f = new Fixture();
        var logger = new CommunicationActivityFixture.EnabledLogger<KnowledgeSourceCaptureLimitsConfigurationService>();
        var service = new KnowledgeSourceCaptureLimitsConfigurationService(f, f.Preferences, f, f.Time, logger);
        f.Files.Values["knowledge-capture-" + f.Source.Id.ToString("N") + ".txt"] = "malformed";
        await FluentActions.Awaiting(() => service.GetAsync("Team", () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
        logger.Messages.Should().Equal("Source capture limits are unreadable or unconfirmed; refresh remains unavailable.");
        f.Files.Writes.Should().Be(0);
        f.Events.Should().BeEmpty();
        f.BodyReads.Should().Be(0);
        f.SourceWrites.Should().Be(0);
    }

    [Fact]
    public void ExactGuidPreferencesUseSharedStoreAndRemoveOnlyTheirOwnNumericFiles()
    {
        var files = new Files();
        var preferences = new LocalKnowledgeSourceCaptureLimitsPreferences(files);
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        preferences.BeginWrite(id);
        preferences.Save(id, Lowered);
        preferences.Invoking(p => p.Load(id)).Should().Throw<InvalidDataException>();
        preferences.ReadBack(id).Should().Be(Lowered);
        preferences.ConfirmWrite(id);
        preferences.Save(other, new(1, 1, 1));
        preferences.Remove(id);
        preferences.Load(id).Should().BeNull();
        preferences.Load(other).Should().NotBeNull();
        preferences.Invoking(p => p.Load(Guid.Empty)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task StaleNativeSelectedRowCannotSubstituteNewSameNamedSource()
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var f = new Fixture();
        var selected = f.Source;
        (await f.Service.GetAsync("Team", () => true, Token, selected)).Source.Id.Should().Be(selected.Id);
        f.Source = f.Source with { Id = Guid.NewGuid() };
        await FluentActions.Awaiting(() => f.Service.ReviewAsync("Team", Lowered, () => true, Token, selected))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public void SharedAtomicSourcePreferenceSurvivesRestartWithoutTouchingOtherPreferences()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "knowledge-capture-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new Paths(root, root);
            var preferences = new LocalKnowledgeSourceCaptureLimitsPreferences(paths);
            var id = Guid.NewGuid();
            var other = Guid.NewGuid();
            preferences.Save(other, new(1, 1, 1));
            preferences.BeginWrite(id);
            preferences.Save(id, Lowered);
            preferences.ReadBack(id).Should().Be(Lowered);
            new LocalKnowledgeSourceCaptureLimitsPreferences(paths).Invoking(p => p.Load(id)).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite(id);
            new LocalKnowledgeSourceCaptureLimitsPreferences(paths).Load(id).Should().Be(Lowered);
            Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
            preferences.Remove(id);
            preferences.Load(id).Should().BeNull();
            preferences.Load(other).Should().NotBeNull();
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    [Fact]
    public void NumericCleanupReadbackCannotClaimRemovalWhenSavedTupleRemains()
    {
        var files = new Files();
        var preferences = new LocalKnowledgeSourceCaptureLimitsPreferences(files);
        var id = Guid.NewGuid();
        preferences.Save(id, Lowered);
        files.IgnoreDelete = true;
        preferences.Invoking(p => p.Remove(id)).Should().Throw<InvalidDataException>();
    }

    private sealed record Paths(string LocalRoot, string RoamingRoot) : Kora.Core.Dependencies.IApplicationDataPaths;

    private static KnowledgeSourceMetadata Metadata(string identity = "root") => new(@"C:\Team", identity,
        KnowledgeSourceScope.ImmediateFiles, [new(@"C:\Team", identity)], [new(@"C:\Team\a.md", "file", 10, DateTimeOffset.UnixEpoch)]);

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Files : IPreferenceStore
    {
        internal readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
        internal int Writes;
        internal int ThrowWriteAt;
        internal Action? AfterWrite;
        internal Action? AfterDelete;
        internal bool IgnoreDelete;
        public string? ReadText(string fileName) => Values.GetValueOrDefault(fileName);
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteText(string fileName, string contents)
        {
            Writes++;
            if (Writes == ThrowWriteAt) { throw new IOException("Synthetic preference failure."); }
            Values[fileName] = contents;
            AfterWrite?.Invoke();
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) { if (!IgnoreDelete) { Values.Remove(fileName); } AfterDelete?.Invoke(); }
    }
    private sealed class Fixture : IKnowledgeSourceStore, ISecurityAuditLog
    {
        internal KnowledgeSource Source = new(Guid.NewGuid(), 1, "Team", Metadata(), KnowledgeSourceState.Enabled, DateTimeOffset.UnixEpoch, 100);
        internal readonly Files Files = new();
        internal readonly Clock Time = new();
        internal readonly LocalKnowledgeSourceCaptureLimitsPreferences Preferences;
        internal readonly KnowledgeSourceCaptureLimitsConfigurationService Service;
        internal readonly List<SecurityAuditEvent> Events = [];
        internal bool Duplicate;
        internal int Lists;
        internal int BodyReads;
        internal int SourceWrites;
        internal int ThrowAuditAt;
        internal Action? AfterAudit;
        internal Task? ListWait;
        internal Fixture() { Preferences = new(Files); Service = NewService(); }
        internal KnowledgeSourceCaptureLimitsConfigurationService NewService() => new(this, Preferences, this, Time,
            NullLogger<KnowledgeSourceCaptureLimitsConfigurationService>.Instance);
        public async Task<IReadOnlyList<KnowledgeSource>> ListAsync(CancellationToken cancellationToken)
        {
            Lists++;
            if (ListWait is not null) { await ListWait; }
            var copy = Source with { Folder = new(Source.Folder.CanonicalPath, Source.Folder.DirectoryIdentity,
                Source.Folder.Scope, Source.Folder.Directories, Source.Folder.Files, Source.Folder.CaptureLimits) };
            return Duplicate ? [copy, copy with { Id = Guid.NewGuid() }] : [copy];
        }
        public Task<KnowledgeSourceRevision> ReadAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        { BodyReads++; throw new NotSupportedException(); }
        public Task<KnowledgeSource> BeginRefreshAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        { SourceWrites++; throw new NotSupportedException(); }
        public Task<KnowledgeSource> PublishAsync(KnowledgeSource? expected, string name, KnowledgeSourceRevision snapshot,
            IReadOnlyList<ReadOnlyMemory<byte>> originalBytes, CancellationToken cancellationToken)
        { SourceWrites++; throw new NotSupportedException(); }
        public Task DisableAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        { SourceWrites++; throw new NotSupportedException(); }
        public Task RemoveAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        { SourceWrites++; throw new NotSupportedException(); }
        public void Write(SecurityAuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            if (Events.Count == ThrowAuditAt) { throw new IOException("Synthetic unavailable audit."); }
            AfterAudit?.Invoke();
        }
    }
}
