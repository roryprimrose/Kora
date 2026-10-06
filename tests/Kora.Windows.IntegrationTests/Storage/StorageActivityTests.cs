using System.Collections.Concurrent;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class StorageActivityTests
{
    [WindowsFact]
    public async Task Host_bound_storage_preserves_the_host_session_and_child_context()
    {
        using var fixture = new OwnedStorageFixture();
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        HostActivity.ConfigureW3C();
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        using var parent = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var storage = stopped.Where(activity => activity.TraceId == parent.Activity!.TraceId).Should().ContainSingle().Which;
        storage.OperationName.Should().Be("storage.commit");
        storage.GetTagItem("kora.session.id").Should().Be(request.SessionId.Value);
        storage.ParentSpanId.Should().Be(parent.Activity!.SpanId);
        storage.Status.Should().Be(ActivityStatusCode.Ok);
        HostActivity.RequireCurrent().Should().BeSameAs(parent);
        parent.Complete(HostOperationOutcome.Completed);
    }

    [WindowsFact]
    public async Task Storage_boundaries_preserve_async_parentage_and_end_without_content_or_identity_tags()
    {
        using var fixture = new OwnedStorageFixture();
        using var parent = new Activity("test.storage-parent").SetIdFormat(ActivityIdFormat.W3C).Start();
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = CreateListener(parent.TraceId, stopped);
        using var key = await new WindowsStorageKeyStore(fixture).CreateNewAsync(TestContext.Current.CancellationToken);
        var store = new WindowsEncryptedArtifactStore(fixture, key);
        var reference = await store.PublishAsync(OwnedStorageFixture.NewIdentity(), "private content"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await store.ReadAsync(reference, TestContext.Current.CancellationToken);
        await store.ReconcileAsync([reference], TestContext.Current.CancellationToken);

        var activities = stopped.ToArray();
        activities.Should().HaveCount(4);
        foreach (var activity in activities)
        {
            activity.IdFormat.Should().Be(ActivityIdFormat.W3C);
            activity.ParentSpanId.Should().Be(parent.SpanId);
            activity.Status.Should().Be(ActivityStatusCode.Ok);
            activity.TagObjects.Should().BeEmpty();
            activity.Baggage.Should().BeEmpty();
        }
        ReferenceEquals(Activity.Current, parent).Should().BeTrue();
    }

    [WindowsFact]
    public async Task A_cancelled_key_operation_ends_with_error_status_and_restores_its_parent()
    {
        using var fixture = new OwnedStorageFixture();
        using var parent = new Activity("test.storage-parent").SetIdFormat(ActivityIdFormat.W3C).Start();
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = CreateListener(parent.TraceId, stopped);
        using var source = new CancellationTokenSource();
        source.Cancel();
        var create = () => new WindowsStorageKeyStore(fixture).CreateNewAsync(source.Token);
        await create.Should().ThrowAsync<OperationCanceledException>();
        var activity = stopped.Should().ContainSingle().Which;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.ParentSpanId.Should().Be(parent.SpanId);
        ReferenceEquals(Activity.Current, parent).Should().BeTrue();
    }

    private static ActivityListener CreateListener(ActivityTraceId traceId, ConcurrentQueue<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static source => string.Equals(source.Name, "Kora.Windows", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    stopped.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
