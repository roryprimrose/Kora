using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task ExactDigestPassesThroughVolatileQueryWithoutBodyReadsAndRejectsChangedPredicate()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var summary = CatalogueSummary(fixture);
        var query = new SessionAttachmentQuery(true, summary.Session.Value.ToString("D"), SessionAttachmentFilter.Active,
            "text/", summary.MediaType, AttachmentCopyStatus.Confirmed, 0, summary.StoredBytes, summary.File.Digest);
        fixture.CataloguePage = new(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(fixture.Service, files);
        (await service.Read(query, null, () => true, fixture.Token)).Should().BeSameAs(fixture.CataloguePage);
        fixture.ObservedAttachmentQuery.Should().BeSameAs(query);
        foreach (var digest in new string?[] { null, new string('b', 64) })
        {
            fixture.CataloguePage = fixture.CataloguePage with
            {
                Query = new(true, query.ExactSessionId, query.Filter, "text/", summary.MediaType,
                    AttachmentCopyStatus.Confirmed, 0, summary.StoredBytes, digest), Records = []
            };
            var mismatch = () => service.Read(query, null, () => true, fixture.Token);
            await mismatch.Should().ThrowAsync<InvalidDataException>();
        }
        fixture.FileReads.Should().Be(0);
        fixture.ControlCalls.Should().Be(0);
        fixture.PassiveInspectionCalls.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AttachmentCopyStatus.Confirmed)]
    [InlineData(AttachmentCopyStatus.Held)]
    [InlineData(AttachmentCopyStatus.Unavailable)]
    public async Task CopyAndByteQueryIsForwardedExactlyAndMismatchRefusesWithoutBodyOrWrites(AttachmentCopyStatus copy)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var summary = CatalogueSummary(fixture) with
        { CopyStatus = copy, StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : 4 };
        var query = new SessionAttachmentQuery(true, summary.Session.Value.ToString("D"), SessionAttachmentFilter.Active,
            copyStatus: copy, minimumStoredBytes: copy == AttachmentCopyStatus.Unavailable ? null : 0, maximumStoredBytes: 4);
        fixture.CataloguePage = new(query, query.Matches(summary) ? [summary] : [], null,
            DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(fixture.Service, files);
        (await service.Read(query, null, () => true, fixture.Token)).Should().BeSameAs(fixture.CataloguePage);
        fixture.ObservedAttachmentQuery.Should().BeSameAs(query);
        foreach (var changed in new[]
        {
            new SessionAttachmentQuery(true, query.ExactSessionId, query.Filter, maximumStoredBytes: 4),
            new(true, query.ExactSessionId, query.Filter, copyStatus: copy, maximumStoredBytes: 3),
            new(true, query.ExactSessionId, query.Filter, copyStatus: copy, minimumStoredBytes: 1, maximumStoredBytes: 4)
        })
        {
            fixture.CataloguePage = fixture.CataloguePage with { Query = changed, Records = [] };
            var mismatch = () => service.Read(query, null, () => true, fixture.Token);
            await mismatch.Should().ThrowAsync<InvalidDataException>();
        }
        fixture.FileReads.Should().Be(0);
        fixture.ControlCalls.Should().Be(0);
        fixture.PassiveInspectionCalls.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    public async Task ExactMediaQueryPassesUnchangedAndRefusesMismatchedFilterWithoutBodyOrAuthority(string type)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var summary = CatalogueSummary(fixture) with { MediaType = type };
        var query = new SessionAttachmentQuery(true, summary.Session.Value.ToString("D"),
            SessionAttachmentFilter.Active, "text/", type);
        fixture.CataloguePage = new(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 2, false);
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(fixture.Service, files);
        (await service.Read(query, null, () => true, fixture.Token)).Records.Should().ContainSingle();
        fixture.ObservedAttachmentQuery.Should().BeSameAs(query);
        fixture.CataloguePage = fixture.CataloguePage with
        {
            Query = new(true, query.ExactSessionId, query.Filter, query.MetadataText),
            Records = []
        };
        var mismatch = () => service.Read(query, null, () => true, fixture.Token);
        await mismatch.Should().ThrowAsync<InvalidDataException>();
        fixture.FileReads.Should().Be(0);
        fixture.ControlCalls.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task LiteralAttachmentQueryPassesThroughWithoutContentReadsAuthorityOrAuditAndRejectsMismatchedText()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var summary = CatalogueSummary(fixture);
        var query = new SessionAttachmentQuery(true, summary.Session.Value.ToString("D"),
            SessionAttachmentFilter.Active, "text/");
        fixture.CataloguePage = new(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 2, false);
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(fixture.Service, files);
        (await service.Read(query, null, () => true, fixture.Token)).Records.Should().ContainSingle();
        fixture.ObservedAttachmentQuery.Should().BeSameAs(query);
        fixture.CataloguePage = fixture.CataloguePage with { Query = new(true, query.ExactSessionId, query.Filter, "text") };
        var mismatch = () => service.Read(query, null, () => true, fixture.Token);
        await mismatch.Should().ThrowAsync<InvalidDataException>();
        fixture.FileReads.Should().Be(0);
        fixture.ControlCalls.Should().Be(0);
        fixture.PassiveInspectionCalls.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, SessionAttachmentFilter.All)]
    [InlineData(true, SessionAttachmentFilter.Active)]
    [InlineData(false, SessionAttachmentFilter.Done)]
    [InlineData(true, SessionAttachmentFilter.Removed)]
    public async Task ExactAttachmentQueryIsForwardedByActualConsumerAndRemainsMetadataOnly(bool includeRemoved, SessionAttachmentFilter filter)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var summary = CatalogueSummary(fixture);
        if (filter == SessionAttachmentFilter.Done) { summary = summary with { SessionState = AttachmentSessionState.Done }; }
        if (filter == SessionAttachmentFilter.Removed)
        {
            summary = summary with { SessionState = AttachmentSessionState.Removed, CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 };
        }
        var query = new SessionAttachmentQuery(includeRemoved, summary.Session.Value.ToString("D"), filter);
        fixture.CataloguePage = new(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 2, false);
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(fixture.Service, files);
        (await service.Read(query, null, () => true, fixture.Token)).Query.Should().Be(query);
        fixture.ObservedAttachmentQuery.Should().BeSameAs(query);
        fixture.FileReads.Should().Be(0);
        fixture.ControlCalls.Should().Be(0);
        fixture.PassiveInspectionCalls.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
        fixture.CataloguePage = fixture.CataloguePage with { Query = new(includeRemoved, Guid.NewGuid().ToString("D")), Records = [] };
        var mismatched = () => service.Read(query, null, () => true, fixture.Token);
        await mismatched.Should().ThrowAsync<InvalidDataException>();
        fixture.CataloguePage = fixture.CataloguePage with { Query = new(includeRemoved, query.ExactSessionId,
            filter == SessionAttachmentFilter.All ? SessionAttachmentFilter.Active : SessionAttachmentFilter.All) };
        await mismatched.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogueIsMetadataOnlyAndExactNavigationUsesCurrentPrivateBodyPathWithoutResume(bool done)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        fixture.RetainedFile = FileRecord(fixture.Request);
        var summary = CatalogueSummary(fixture);
        if (done)
        {
            summary = summary with { SessionState = AttachmentSessionState.Done };
            fixture.ExactListReader = _ => new(fixture.Session with { IsActive = false }, null);
        }
        fixture.CataloguePage = new(new(), [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(fixture.Service, files);
        var page = await service.Read(new(), null, () => true, fixture.Token);
        page.Should().BeSameAs(fixture.CataloguePage);
        fixture.FileReads.Should().Be(0);
        fixture.CatalogueReads.Should().Be(1);
        fixture.PassiveInspectionCalls.Should().Be(0, "listing must not invoke the broad full-body retention read");
        var target = await service.ResolveExact(summary, () => true, fixture.Token);
        target.Authority.SessionId.Should().Be(summary.Session);
        target.Authority.IsActive.Should().Be(!done);
        fixture.FileReads.Should().Be(1);
        fixture.CatalogueVerifications.Should().Be(2);
        fixture.ControlCalls.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
        audit.Events.Should().OnlyContain(item => item.ActionId == "session.file.read");
    }

    [Theory]
    [InlineData("initial-private")]
    [InlineData("late-private")]
    [InlineData("late-control")]
    [InlineData("late-admission")]
    [InlineData("cancel")]
    [InlineData("malformed")]
    [InlineData("query")]
    [InlineData("port")]
    [InlineData("hostile")]
    public async Task CatalogueRejectsClosedCorruptCancelledOrUncomposedReadsWithoutBodyFallback(string boundary)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var gate = true;
        var summary = CatalogueSummary(fixture);
        fixture.CataloguePage = new(new(), [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
        if (boundary is "initial-private") { fixture.CanInspect = false; }
        fixture.BeforeCatalogueRead = () =>
        {
            if (boundary is "late-private") { fixture.CanInspect = false; }
            if (boundary is "late-control") { fixture.AdvanceRevision(); }
            if (boundary is "late-admission") { gate = false; }
            if (boundary is "cancel") { throw new OperationCanceledException(); }
            if (boundary is "malformed") { fixture.CataloguePage = fixture.CataloguePage with { Records = default }; }
            if (boundary is "query") { fixture.CataloguePage = fixture.CataloguePage with { Query = new(true) }; }
        };
        var sessions = boundary is "port"
            ? new SessionWorkspaceService(new NoFileStore(fixture), new(fixture), fixture, fixture.Logger) : fixture.Service;
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(sessions, files);
        using var hostile = boundary is "hostile"
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request) : null;
        var read = () => service.Read(new(), null, () => gate, fixture.Token);
        await read.Should().ThrowAsync<Exception>();
        fixture.FileReads.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("lifecycle")]
    [InlineData("missing")]
    [InlineData("replaced")]
    [InlineData("held")]
    [InlineData("late-admission")]
    [InlineData("late-cancel")]
    public async Task CatalogueNavigationRefusesStaleSummaryOrLateAdmissionRatherThanOpeningLatestAttachment(string boundary)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        fixture.RetainedFile = FileRecord(fixture.Request);
        var summary = CatalogueSummary(fixture);
        var gate = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        fixture.ExactListReader = _ => boundary switch
        {
            "session" => new(fixture.Session with { SessionId = new(Guid.NewGuid()) }, null),
            "generation" => new(fixture.Session with { Generation = new(2) }, null),
            "lifecycle" => new(fixture.Session with { IsActive = false }, null),
            _ => new(fixture.Session, null),
        };
        fixture.BeforeCatalogueVerify = () =>
        {
            if (boundary is "held") { throw new InvalidOperationException("Copy certification held."); }
            if (fixture.CatalogueVerifications != 2) { return; }
            if (boundary is "late-admission") { gate = false; }
            if (boundary is "late-cancel") { cancellation.Cancel(); }
        };
        if (boundary is "missing") { fixture.RetainedFile = null; }
        if (boundary is "replaced") { fixture.RetainedFile = FileRecord(fixture.Request); }
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(fixture.Service, files);
        var navigate = () => service.ResolveExact(summary, () => gate, cancellation.Token);
        await navigate.Should().ThrowAsync<Exception>();
        fixture.ControlCalls.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task CatalogueRejectsCancellationAfterMetadataReadAndMissingSelection()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        fixture.BeforeCatalogueRead = cancellation.Cancel;
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var service = new SessionAttachmentCatalogueService(fixture.Service, files);
        var read = () => service.Read(new(), null, () => true, cancellation.Token);
        await read.Should().ThrowAsync<OperationCanceledException>();
        var navigate = () => service.ResolveExact(null!, () => true, fixture.Token);
        await navigate.Should().ThrowAsync<ArgumentNullException>();
        var missingAdmission = () => service.ResolveExact(CatalogueSummary(fixture), null!, fixture.Token);
        await missingAdmission.Should().ThrowAsync<ArgumentNullException>();
        var missingQuery = () => service.Read(null!, null, () => true, fixture.Token);
        await missingQuery.Should().ThrowAsync<ArgumentNullException>();
        var missingReadAdmission = () => service.Read(new(), null, null!, fixture.Token);
        await missingReadAdmission.Should().ThrowAsync<ArgumentNullException>();
    }

    private static SessionAttachmentSummary CatalogueSummary(Fixture fixture)
    {
        var attachment = fixture.RetainedFile ?? FileRecord(fixture.Request);
        return new(Guid.NewGuid(), new(Guid.NewGuid()), attachment.Session, new(1), attachment.Generation,
            attachment.StorageRevision, AttachmentSessionState.Active, attachment.File.Reference,
            attachment.File.Review.Metadata.ByteLength, "text/markdown", attachment.File.AdmittedAt, AttachmentCopyStatus.Confirmed);
    }

    private partial class Fixture : ISessionAttachmentCatalogueStore
    {
        internal SessionAttachmentPage CataloguePage { get; set; } = new(new(), [], null, DateTimeOffset.UnixEpoch, 0, new string('0', 64), 0, false);
        internal Action? BeforeCatalogueRead { get; set; }
        internal Action? BeforeCatalogueVerify { get; set; }
        internal int CatalogueReads { get; private set; }
        internal int CatalogueVerifications { get; private set; }
        internal SessionAttachmentQuery? ObservedAttachmentQuery { get; private set; }

        public ValueTask<SessionAttachmentPage> ReadAttachmentPage(SessionAttachmentQuery query, string? cursor,
            Func<bool> admitted, CancellationToken token)
        {
            CatalogueReads++;
            ObservedAttachmentQuery = query;
            BeforeCatalogueRead?.Invoke();
            return ValueTask.FromResult(CataloguePage);
        }

        public ValueTask VerifyAttachmentSummary(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
        {
            CatalogueVerifications++;
            BeforeCatalogueVerify?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
