using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.Voice;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Voice;

[Collection("Host tracing")]
public sealed class AudioControlAdmissionTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public AudioControlAdmissionTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Original_provenance_real_durable_session_and_new_requests_ignore_incoming_trace_identity()
    {
        var store = new AudioControlTestStore();
        await using var admission = new AudioControlAdmission(store, store, new(store));
        var incoming = HostRequest.Create(RequestOrigin.ActivatedVoice);
        using var ambient = HostActivity.BeginRoot(incoming, HostActivityLayer.Application, HostOperation.Request);
        var incomingTrace = Activity.Current!.TraceId;
        var result = await admission.RunAsync(RequestOrigin.LocalUi, static () => true, (request, authority) =>
        {
            request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            request.SessionId.Should().Be(authority.SessionId);
            request.SessionId.Should().NotBe(incoming.SessionId);
            Activity.Current!.TraceId.Should().NotBe(incomingTrace);
            return authority;
        }, TestContext.Current.CancellationToken);
        var second = await admission.RunAsync(RequestOrigin.LocalUi, static () => true,
            (request, _) => request, TestContext.Current.CancellationToken);
        second.SessionId.Should().Be(result.SessionId);
        second.RequestId.Should().NotBe(store.Tasks[0].Request.RequestId);
        store.Tasks.Where(record => record.IsTerminal).Should().OnlyContain(record => record.State == HostTaskState.Succeeded);
        ambient.Complete(HostOperationOutcome.Completed);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData((RequestOrigin)99)]
    public async Task Nonlocal_origin_never_creates_durable_authority(RequestOrigin origin)
    {
        var store = new AudioControlTestStore();
        await using var admission = new AudioControlAdmission(store, store, new(store));
        var run = () => admission.RunAsync(origin, static () => true, static (_, _) => true, TestContext.Current.CancellationToken);
        await run.Should().ThrowAsync<InvalidOperationException>();
        store.Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_owner_changed_live_owner_generation_and_receipt_failure_fail_closed()
    {
        var store = new AudioControlTestStore();
        await using var admission = new AudioControlAdmission(store, store, new(store));
        var denied = () => admission.RunAsync(RequestOrigin.LocalUi, static () => false, static (_, _) => true, CancellationToken.None);
        await denied.Should().ThrowAsync<InvalidOperationException>();
        store.Tasks.Should().BeEmpty();
        var eligible = true;
        store.BeforeOperation = () => eligible = false;
        var changed = () => admission.RunAsync(RequestOrigin.LocalUi, () => eligible, static (_, _) => true, CancellationToken.None);
        await changed.Should().ThrowAsync<InvalidOperationException>();
        store.Tasks.Last().State.Should().Be(HostTaskState.Failed);
        store.BeforeOperation = null;
        eligible = true;
        store.FailTerminal = true;
        await changed.Should().ThrowAsync<IOException>();
        store.FailTerminal = false;
        await changed.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cancellation_and_disposal_do_not_restart_or_revive_captured_callbacks()
    {
        var store = new AudioControlTestStore();
        var admission = new AudioControlAdmission(store, store, new(store));
        await admission.DisposeAsync();
        await admission.DisposeAsync();
        var disposed = () => admission.RunAsync(RequestOrigin.LocalUi, static () => true, static (_, _) => true, CancellationToken.None);
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
        var cancellable = new AudioControlAdmission(store, store, new(store));
        await using var owned = cancellable;
        using var cancellation = new CancellationTokenSource();
        var run = () => cancellable.RunAsync<bool>(RequestOrigin.LocalUi, static () => true,
            (_, _) => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }, cancellation.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        store.Tasks.Last().IsTerminal.Should().BeFalse();
    }

    [Fact]
    public async Task Creation_receipt_failure_preserves_real_identity_but_never_reissues_control_authority()
    {
        var store = new AudioControlTestStore { FailTerminal = true };
        await using var admission = new AudioControlAdmission(store, store, new(store));
        var run = () => admission.RunAsync(RequestOrigin.LocalUi, static () => true, static (_, _) => true, CancellationToken.None);
        await run.Should().ThrowAsync<IOException>();
        store.Authority.Should().NotBeNull();
        store.FailTerminal = false;
        await run.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Failed_session_creation_is_not_replaced_by_a_new_candidate_or_trace()
    {
        var store = new AudioControlTestStore { CreateFailure = new InvalidDataException("unknown durable admission") };
        await using var admission = new AudioControlAdmission(store, store, new(store));
        var run = () => admission.RunAsync(RequestOrigin.LocalUi, static () => true, static (_, _) => true, CancellationToken.None);
        await run.Should().ThrowAsync<InvalidDataException>();
        store.CreateFailure = null;
        await run.Should().ThrowAsync<InvalidOperationException>();
        store.Tasks.Should().ContainSingle();
    }
}
