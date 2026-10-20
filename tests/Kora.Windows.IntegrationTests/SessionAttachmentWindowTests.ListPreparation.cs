using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Tools.Files;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionAttachmentWindowTests
{
    [WindowsFact]
    public async Task ListPreparationDoesNotRetireAnExistingNativeFileSecurityReview()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sessions = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var capture = new Capture();
            var audit = new Audit();
            using var preview = new LocalFilePreview(capture, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance);
            var search = new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, fixture.Time, NullLogger<LocalFileSearch>.Instance);
            await using var service = new SessionFileAttachmentService(sessions, access,
                new LocalSessionFileAttach(capture, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance),
                search, fixture.Time, audit, NullLogger<SessionFileAttachmentService>.Instance);
            var failures = new List<string>();
            using var controller = new SessionAttachmentWindowController(failures.Add, service, sessions, access, new Picker());
            var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(sessions,
                new(new WindowsSqliteEvidenceReader(sink), access, fixture.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance, attachments: controller);
            LocalFilePreviewWindow? opened = null;
            using var windows = Window.WindowOpenedEvent.AddClassHandler<LocalFilePreviewWindow>((window, _) => opened = window);
            try
            {
                model.ListSearchKind = SessionListSearchKind.Browse;
                await model.SearchListAsync();
                await model.SelectAsync(model.Sessions.Single());
                var selected = model.SelectedSessionRecord!;
                await controller.Open(selected, attach: true, () => true, fixture.Token);
                var review = service.Review;
                review.Should().NotBeNull();
                (await model.PrepareListQueryAsync(fixture.Request.SessionId, () => true,
                    () => throw new InvalidOperationException("A pending review must not be replaced."))).Should().BeFalse();
                model.SelectedSessionRecord.Should().BeSameAs(selected);
                model.ListSearchKind.Should().Be(SessionListSearchKind.Browse);
                service.Review.Should().BeSameAs(review);
                opened!.IsVisible.Should().BeTrue();
                capture.Reads.Should().Be(0);
                fixture.Count("session_file").Should().Be(0);
                failures.Should().BeEmpty();
            }
            finally { model.Close(); }
        });
    }
}
