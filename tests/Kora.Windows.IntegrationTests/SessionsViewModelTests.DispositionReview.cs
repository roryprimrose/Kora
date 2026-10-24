using System.Collections.Concurrent;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsViewModelTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("owner")]
    [InlineData("original")]
    [InlineData("cancel")]
    [InlineData("selection")]
    public async Task LateDispositionPreviewCallbacksDiscardExactPendingPreviewAfterAnyLifetimeFence(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var held = new HeldStore();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        using var cancellation = new CancellationTokenSource();
        var owner = true;
        var admitted = true;
        using var capture = new DispositionActivityCapture();
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var read = viewer.ReviewDispositionAsync(new(f.Request.SessionId.Value, 1, 0), RequestOrigin.ActivatedVoice,
            () => admitted, () => owner, cancellation.Token);
        held.DispositionRequest!.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        held.DispositionRequest.SessionId.Should().Be(f.Request.SessionId);
        held.DispositionRequest.Should().NotBe(original.Request);
        capture.Snapshot.Should().Contain(activity => activity.Links.Any(link => link.Context == original.Activity!.Context));
        switch (change)
        {
            case "close": viewer.Close(); break;
            case "private": access.CanInspect = false; break;
            case "control": access.CanControl = false; break;
            case "revision": access.ControlRevision++; break;
            case "owner": owner = false; break;
            case "original": admitted = false; break;
            case "cancel": cancellation.Cancel(); break;
            case "selection": await viewer.SelectAsync(new(new(new(Guid.NewGuid()), new(1), true), null)); break;
        }
        var preview = new SessionDispositionPreview(Guid.NewGuid(),
            new(new(f.Request.SessionId, new(1), true), null), "exact-store-revision", 1, 2, 3, 4)
        { Attachments = 5, ProviderProfiles = 6, ConversationMessages = 7, SavedConversationDrafts = 8 };
        held.DispositionCompletion.SetResult(preview);
        (await read).Should().Be(change is "none");
        viewer.CanConfirmDisposition.Should().Be(change is "none");
        if (change is "none")
        {
            viewer.Detail.Should().Contain("questions 1").And.Contain("scoped grants 2")
                .And.Contain("observations 3").And.Contain("waits 4")
                .And.Contain("durable file attachments 5").And.Contain("provider overrides 6")
                .And.Contain("conversation messages 7").And.Contain("saved conversation drafts 8");
        }
        else { viewer.Detail.Should().BeEmpty(); }
        service.IsDispositionPreviewCurrent(preview).Should().Be(change is "none");
        held.ControlCalls.Should().Be(0);
        viewer.Close();
    }

    [Fact]
    public async Task DispositionTraceSnapshotRemainsStableWhileConcurrentDesktopActivityIsCaptured()
    {
        var token = TestContext.Current.CancellationToken;
        using var capture = new DispositionActivityCapture();
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Desktop, Kora.Core.Diagnostics.HostOperation.Request);
        var snapshot = capture.Snapshot;
        snapshot.Should().ContainSingle().Which.Should().BeSameAs(original.Activity);
        var enumerating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appended = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(async () =>
        {
            await enumerating.Task.WaitAsync(token);
            using var concurrent = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                Kora.Core.Diagnostics.HostActivityLayer.Desktop, Kora.Core.Diagnostics.HostOperation.Request);
            appended.SetResult(concurrent.Activity!);
        }, token);
        var observed = new List<Activity>();
        try
        {
            foreach (var activity in snapshot)
            {
                enumerating.SetResult();
                await appended.Task.WaitAsync(token);
                observed.Add(activity);
            }
        }
        finally
        {
            enumerating.TrySetResult();
            await writer;
        }
        observed.Should().Equal(original.Activity!);
        snapshot.Should().ContainSingle().Which.Should().BeSameAs(original.Activity);
        capture.Snapshot.Should().Equal(original.Activity!, await appended.Task);
    }

    private sealed class DispositionActivityCapture : IDisposable
    {
        private readonly ConcurrentQueue<Activity> activities = new();
        private readonly ActivityListener listener;

        internal DispositionActivityCapture()
        {
            listener = new()
            {
                ShouldListenTo = source => string.Equals(source.Name, "Kora.Desktop", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activities.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
        }

        internal IReadOnlyList<Activity> Snapshot => activities.ToArray();
        public void Dispose() => listener.Dispose();
    }
}
