using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task ExactAttachmentCommandUsesHostObservedProvenanceWithoutRenewingActiveOrDone(RequestOrigin origin, bool done)
    {
        using var fixture = new Fixture();
        fixture.RetainedFile = FileRecord(fixture.Request);
        var exact = CatalogueSummary(fixture) with
        { SessionState = done ? AttachmentSessionState.Done : AttachmentSessionState.Active };
        fixture.Session = fixture.Session with { IsActive = !done };
        var query = new SessionAttachmentQuery(exactSessionId: exact.Session.Value.ToString("D"));
        fixture.CataloguePage = new(query, [exact], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
        var audit = new FileAudit();
        var capture = new FileCapture();
        await using var files = FileService(fixture, FileAction(capture, audit), audit);
        var viewer = new RetainedViewer
        {
            Opening = (target, observed, live) =>
            {
                target.Authority.Should().Be(fixture.Session);
                observed.Should().BeSameAs(exact);
                live().Should().BeTrue();
                HostActivity.Current!.Request.Origin.Should().Be(origin);
                HostActivity.Current.Request.SessionId.Should().Be(exact.Session);
                return Task.CompletedTask;
            },
        };
        var service = new SessionAttachmentCommandService(fixture.Service, files, fixture, viewer);
        await service.Execute(new(SessionCommandOperation.AttachmentInspect, exact.Session.Value), origin, () => true, fixture.Token);
        viewer.Calls.Should().Be(1);
        fixture.CatalogueVerifications.Should().Be(2);
        fixture.TaskWrites.Should().BeEmpty();
        fixture.ControlCalls.Should().Be(0);
        fixture.Session.IsActive.Should().Be(!done);
        capture.Disposals.Should().Be(0);
        audit.Events.Should().OnlyContain(item => item.ActionId == "session.file.read"
            && item.Initiator == (origin == RequestOrigin.ActivatedVoice ? Kora.Core.Auditing.SecurityAuditInitiator.VoiceCommand
                : Kora.Core.Auditing.SecurityAuditInitiator.LocalUser));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("no-id")]
    [InlineData("empty-id")]
    [InlineData("private")]
    [InlineData("input")]
    [InlineData("pending-review")]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("held")]
    [InlineData("removed")]
    [InlineData("source-swap")]
    [InlineData("storage-revision")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("lifecycle")]
    [InlineData("late-input")]
    [InlineData("late-review")]
    [InlineData("late-open")]
    [InlineData("late-cancel")]
    [InlineData("late-control")]
    [InlineData("cancel")]
    [InlineData("corrupt")]
    public async Task ExactCommandRefusesUnavailableChangedOrLateAuthorityWithoutAnyFallback(string boundary)
    {
        using var fixture = new Fixture();
        fixture.RetainedFile = FileRecord(fixture.Request);
        var exact = CatalogueSummary(fixture);
        var query = new SessionAttachmentQuery(exactSessionId: exact.Session.Value.ToString("D"));
        fixture.CataloguePage = new(query, [exact], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
        var audit = new FileAudit();
        var action = FileAction(new FileCapture(), audit);
        await using var files = FileService(fixture, action, audit);
        var admitted = true;
        using var cancellation = new CancellationTokenSource();
        if (boundary is "private") { fixture.CanInspect = false; }
        if (boundary is "input") { admitted = false; }
        if (boundary is "missing") { fixture.CataloguePage = fixture.CataloguePage with { Records = [] }; }
        if (boundary is "foreign") { fixture.CataloguePage = fixture.CataloguePage with { Records = [exact with { Session = new(Guid.NewGuid()) }] }; }
        if (boundary is "held") { fixture.CataloguePage = fixture.CataloguePage with { Records = [exact with { CopyStatus = AttachmentCopyStatus.Held }] }; }
        if (boundary is "removed") { fixture.RetainedFile = null; }
        if (boundary is "source-swap") { fixture.RetainedFile = FileRecord(fixture.Request); }
        if (boundary is "storage-revision") { fixture.RetainedFile = fixture.RetainedFile! with { StorageRevision = new(2) }; }
        if (boundary is "corrupt") { fixture.BeforeFileRead = () => throw new InvalidDataException("content must stay private"); }
        fixture.ExactListReader = _ => new(boundary switch
        {
            "session" => fixture.Session with { SessionId = new(Guid.NewGuid()) },
            "generation" => fixture.Session with { Generation = new(2) },
            "lifecycle" => fixture.Session with { IsActive = false },
            _ => fixture.Session,
        }, null);
        if (boundary is "pending-review")
        {
            using var input = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
            await action.Select(new FilePicker(), () => true, fixture.Token);
        }
        if (boundary is "cancel") { cancellation.Cancel(); }
        fixture.BeforeCatalogueVerify = () =>
        {
            if (fixture.CatalogueVerifications != 2) { return; }
            if (boundary is "late-input") { admitted = false; }
        };
        if (boundary is "late-review")
        {
            fixture.FileReader = async () =>
            {
                await action.Select(new FilePicker(), () => true, fixture.Token);
                return fixture.RetainedFile;
            };
        }
        var viewer = new RetainedViewer
        {
            Opening = (_, _, live) =>
            {
                if (boundary is "late-open") { admitted = false; }
                if (boundary is "late-cancel") { cancellation.Cancel(); }
                if (boundary is "late-control") { fixture.AdvanceRevision(); }
                if (boundary is "late-open" or "late-cancel" or "late-control") { live().Should().BeFalse(); }
                return Task.CompletedTask;
            },
        };
        var service = new SessionAttachmentCommandService(fixture.Service, files, fixture, viewer);
        var command = boundary switch
        {
            "invalid" => new SessionCommand(SessionCommandOperation.Help),
            "no-id" => new(SessionCommandOperation.AttachmentInspect),
            "empty-id" => new(SessionCommandOperation.AttachmentInspect, Guid.Empty),
            _ => new(SessionCommandOperation.AttachmentInspect, exact.Session.Value),
        };
        var run = () => service.Execute(command, RequestOrigin.LocalUi, () => admitted, cancellation.Token);
        await run.Should().ThrowAsync<Exception>();
        viewer.Calls.Should().Be(boundary is "late-open" or "late-cancel" or "late-control" ? 1 : 0);
        fixture.TaskWrites.Should().BeEmpty();
        if (boundary is "pending-review") { files.Review.Should().NotBeNull(); }
    }

    [Theory]
    [InlineData("system")]
    [InlineData("model")]
    [InlineData("invocation")]
    [InlineData("relabel")]
    [InlineData("completed")]
    public async Task RetainedReadDoesNotConvertUntrustedOrCompletedAuthorityIntoCaptureConsent(string boundary)
    {
        using var fixture = new Fixture();
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var origin = boundary switch
        {
            "system" => RequestOrigin.HostSystem,
            "model" => (RequestOrigin)99,
            _ => RequestOrigin.ActivatedVoice,
        };
        var request = HostRequest.Create(boundary is "model" ? RequestOrigin.HostSystem : origin);
        if (boundary is "invocation")
        { request = new(request.RequestId, request.SessionId, request.TaskId, origin, new(Guid.NewGuid())); }
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (boundary is "completed") { activity.Complete(HostOperationOutcome.Completed); }
        var service = new SessionAttachmentCommandService(fixture.Service, files, fixture, new RetainedViewer());
        var run = () => service.Execute(new(SessionCommandOperation.AttachmentInspect, fixture.Request.SessionId.Value),
            boundary is "relabel" ? RequestOrigin.LocalUi : origin, () => true, fixture.Token);
        await run.Should().ThrowAsync<InvalidOperationException>();
        fixture.CatalogueReads.Should().Be(0);
    }

    [Fact]
    public async Task MissingExactInspectionStoreCannotFallBackToCatalogueOrBody()
    {
        using var fixture = new Fixture();
        var audit = new FileAudit();
        await using var files = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var workspace = new SessionWorkspaceService(new NoFileStore(fixture), new(fixture), fixture, fixture.Logger);
        var viewer = new RetainedViewer();
        var run = () => new SessionAttachmentCommandService(workspace, files, fixture, viewer)
            .Execute(new(SessionCommandOperation.AttachmentInspect, fixture.Request.SessionId.Value), RequestOrigin.LocalUi,
                () => true, fixture.Token);
        await run.Should().ThrowAsync<InvalidOperationException>();
        viewer.Calls.Should().Be(0);
        fixture.CatalogueReads.Should().Be(0);
        fixture.FileReads.Should().Be(0);
    }

    private sealed class RetainedViewer : IRetainedAttachmentViewer
    {
        internal int Calls { get; private set; }
        internal Func<SessionWorkspaceEntry, SessionAttachmentSummary, Func<bool>, Task> Opening { get; init; }
            = (_, _, _) => Task.CompletedTask;
        public Task Open(SessionWorkspaceEntry target, SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            return Opening(target, exact, admitted);
        }

    }

    private partial class Fixture : ISessionAttachmentInspectionStore
    {
        public ValueTask<SessionAttachmentSummary?> ReadAttachmentInspection(HostId<SessionIdentity> session,
            Func<bool> admitted, CancellationToken token)
        {
            BeforeCatalogueRead?.Invoke();
            return ValueTask.FromResult(CataloguePage.Records.Length == 0 ? null : CataloguePage.Records[0]);
        }
    }
}
