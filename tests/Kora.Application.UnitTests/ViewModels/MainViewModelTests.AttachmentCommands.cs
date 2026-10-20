using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("typed")]
    [InlineData("voice")]
    [InlineData("unprefixed")]
    [InlineData("protected")]
    [InlineData("renamed")]
    [InlineData("late-call")]
    [InlineData("late-name")]
    [InlineData("late-owner")]
    [InlineData("late-dispose")]
    [InlineData("failure")]
    [InlineData("late-failure")]
    [InlineData("unbound")]
    public async Task RetainedInspectionRoutingNeverPublishesSourceInResponseTranscriptSpeechOrModel(string boundary)
    {
        var log = new RetentionPreviewLogger(enabled: boundary is not "unbound");
        var fixture = new Fixture(logger: log);
        await fixture.ViewModel.InitializeAsync();
        var store = BindSessions(fixture);
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("kora.session.id") is Guid id && id == store.Session.Authority.SessionId.Value)
                { activities.Add(activity); }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var capture = new FakeFileInspector();
        using var preview = new LocalFilePreview(capture, fixture.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        await using var files = new SessionFileAttachmentService(workspace, store,
            new(capture, fixture.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance),
            new(preview, new LocalFileLexicalRetrieval(), fixture.Audit, TimeProvider.System, NullLogger<LocalFileSearch>.Instance),
            TimeProvider.System, fixture.Audit, NullLogger<SessionFileAttachmentService>.Instance);
        var request = new HostRequest(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var bytes = Encoding.UTF8.GetBytes(FakeFileInspector.Text);
        store.CommandAttachment = new(request.SessionId, new(1), new(1), new(
            new(Guid.NewGuid(), Guid.NewGuid(), request, new(@"C:\Private\never-read.md", "fake", bytes.Length, DateTimeOffset.UnixEpoch)),
            bytes, DateTimeOffset.UnixEpoch));
        var viewer = new RoutingAttachmentViewer(async () =>
        {
            if (boundary is "late-call") { fixture.CallState.SetState(CallState.Active); }
            if (boundary is "late-name")
            {
                fixture.ViewModel.AssistantNameInput = "Nova";
                await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
            }
            if (boundary is "late-owner") { store.AttachmentInspectionEligible = false; }
            if (boundary is "late-dispose" or "late-failure") { fixture.ViewModel.Dispose(); }
            if (boundary is "failure" or "late-failure") { throw new IOException(FakeFileInspector.Text); }
        });
        if (boundary is not "unbound") { fixture.ViewModel.BindSessionAttachmentCommands(new(workspace, files, store, viewer)); }
        var command = "session attachment inspect " + request.SessionId.Value.ToString("D");
        if (boundary is "protected")
        {
            fixture.CallState.SetState(CallState.Active);
            await fixture.Dispatcher.LastInvocation;
        }
        if (boundary is "renamed")
        {
            fixture.ViewModel.AssistantNameInput = "Nova";
            await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        }
        if (boundary is "typed" or "failure" or "late-failure" or "late-owner" or "late-dispose" or "unbound")
        { await fixture.RunAsync(command); }
        else { await fixture.RaiseActivatedTranscriptAsync((boundary is "unprefixed" ? "" : boundary is "renamed" ? "Nova, " : "Kora, ") + command, 1); }
        if (boundary is "typed" or "voice" or "renamed")
        {
            viewer.Calls.Should().Be(1);
            fixture.ViewModel.ResponseTitle.Should().Be("Retained attachment inspection opened.");
        }
        else { fixture.ViewModel.ResponseTitle.Should().NotBe("Retained attachment inspection opened."); }
        fixture.ViewModel.ResponseBody.Should().NotContain(FakeFileInspector.Text).And.NotContain("never-read.md");
        fixture.ViewModel.Transcript.Should().NotContain(FakeFileInspector.Text);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        capture.Reads.Should().Be(0);
        fixture.HostStore.Records.Should().BeEmpty();
        string.Join("\n", log.Events.Select(entry => entry.Message)).Should().NotContain(FakeFileInspector.Text).And.NotContain("never-read.md");
        log.Events.Where(entry => entry.Name is "RetainedAttachmentInspectionFailed").Should()
            .OnlyContain(entry => entry.Exception == null);
        foreach (var activity in activities)
        {
            string.Join("\n", activity.TagObjects.Select(tag => tag.Key + "=" + tag.Value))
                .Should().NotContain(FakeFileInspector.Text).And.NotContain("never-read.md").And.NotContain(@"C:\Private");
            activity.Baggage.Should().BeEmpty();
            activity.Status.Should().NotBe(ActivityStatusCode.Unset);
        }
    }

    [Theory]
    [InlineData("session attachment inspect selected")]
    [InlineData("Kora, session attachment inspect file:///C:/private/file.md")]
    public async Task InvalidReservedAttachmentInputCannotAnswerPendingQuestion(string command)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        BindSessions(fixture);
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        var count = fixture.Reasoner.Requests.Count;
        await fixture.RunAsync(command);
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not accepted.");
        fixture.Reasoner.Requests.Should().HaveCount(count);
    }

    private sealed class RoutingAttachmentViewer(Func<Task> opened) : IRetainedAttachmentViewer
    {
        internal int Calls { get; private set; }
        public Task Open(SessionWorkspaceEntry target, SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            return opened();
        }
    }

    private sealed partial class SessionCommandStore : ISessionAttachmentCatalogueStore, ISessionAttachmentInspectionStore, ISessionFileStore
    {
        internal bool AttachmentInspectionEligible { get; set; } = true;
        internal SessionFileAttachment? CommandAttachment { get; set; }
        public async ValueTask<SessionAttachmentSummary?> ReadAttachmentInspection(HostId<SessionIdentity> session,
            Func<bool> admitted, CancellationToken token) =>
            (await ReadAttachmentPage(new(exactSessionId: session.Value.ToString("D")), null, admitted, token)).Records[0];
        public ValueTask<SessionAttachmentPage> ReadAttachmentPage(SessionAttachmentQuery query, string? cursor, Func<bool> admitted, CancellationToken token)
        {
            var attachment = CommandAttachment!;
            SessionAttachmentSummary summary = new(Guid.NewGuid(), new(Guid.NewGuid()), attachment.Session,
                Session.Authority.Generation, attachment.Generation, attachment.StorageRevision, AttachmentSessionState.Active,
                attachment.File.Reference, attachment.File.Review.Metadata.ByteLength, "text/markdown", attachment.File.AdmittedAt, AttachmentCopyStatus.Confirmed);
            return ValueTask.FromResult(new SessionAttachmentPage(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false));
        }
        public ValueTask VerifyAttachmentSummary(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<SessionFileAttachment?> ReadAttachment(HostId<SessionIdentity> session, CancellationToken token) => ValueTask.FromResult(CommandAttachment);
        public ValueTask<SessionFileAttachment> Attach(HostRequest request, HostRevision generation, LocalFileRevision revision, ReadOnlyMemory<byte> originalBytes, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionFileAttachment> Replace(HostRequest request, SessionFileRemoval previous, LocalFileRevision revision, ReadOnlyMemory<byte> originalBytes, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionFileRemoval> PreviewRemoval(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask Remove(HostRequest request, SessionFileRemoval review, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
    }
}
