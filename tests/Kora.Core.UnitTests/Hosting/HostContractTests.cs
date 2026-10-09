using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Hosting;

public sealed class HostContractTests
{
    [Fact]
    public void Identities_reject_missing_values_including_default_structs()
    {
        var empty = () => new HostId<SessionIdentity>(Guid.Empty);
        empty.Should().Throw<ArgumentException>();
        var missing = () => default(HostId<RequestIdentity>).Validate();
        missing.Should().Throw<InvalidDataException>();
        var request = () => new HostRequest(default, new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi);
        request.Should().Throw<InvalidDataException>();
        var revision = () => new HostRevision(0);
        revision.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Host_allocates_distinct_business_identities_without_provider_input()
    {
        var first = HostRequest.Create(RequestOrigin.LocalUi);
        var second = HostRequest.Create(RequestOrigin.ActivatedVoice);
        first.RequestId.Should().NotBe(second.RequestId);
        first.SessionId.Should().NotBe(second.SessionId);
        first.TaskId.Should().NotBe(second.TaskId);
        first.Origin.Should().Be(RequestOrigin.LocalUi);
    }

    [Theory]
    [InlineData("exact", true)]
    [InlineData("root", true)]
    [InlineData("invocation", false)]
    [InlineData("omitted", false)]
    [InlineData("request", false)]
    [InlineData("session", false)]
    [InlineData("task", false)]
    [InlineData("origin", false)]
    public void Intent_binding_preserves_root_scope_and_rejects_every_foreign_identity(string scenario, bool expected)
    {
        var root = HostRequest.Create(RequestOrigin.LocalUi);
        var invocation = new HostId<InvocationIdentity>(Guid.NewGuid());
        var intent = new HostRequest(root.RequestId, root.SessionId, root.TaskId, root.Origin,
            scenario is "root" ? null : invocation);
        var request = new HostRequest(
            scenario is "request" ? new(Guid.NewGuid()) : root.RequestId,
            scenario is "session" ? new(Guid.NewGuid()) : root.SessionId,
            scenario is "task" ? new(Guid.NewGuid()) : root.TaskId,
            scenario is "origin" ? RequestOrigin.HostSystem : root.Origin,
            scenario is "omitted" ? null : scenario is "invocation" ? new(Guid.NewGuid()) : invocation);
        request.IsWithinIntent(intent).Should().Be(expected);
        var missing = () => request.IsWithinIntent(null!);
        missing.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(HostTaskState.IntentRecorded, HostTaskState.Interrupted)]
    [InlineData(HostTaskState.DispatchRecorded, HostTaskState.Unknown)]
    public void Recovery_never_infers_success_or_dispatches(HostTaskState before, HostTaskState after)
    {
        var prior = new HostTaskRecord(HostRequest.Create(RequestOrigin.HostSystem), new(1), before);
        var recovered = prior.Recover();
        recovered.State.Should().Be(after);
        recovered.Revision.Value.Should().Be(2);
        recovered.Request.Should().BeSameAs(prior.Request);
        recovered.Recover().Should().BeSameAs(recovered);
    }

    [Theory]
    [InlineData(HostTaskState.Cancelled)]
    [InlineData(HostTaskState.Interrupted)]
    [InlineData(HostTaskState.IntentRecorded)]
    [InlineData(HostTaskState.DispatchRecorded)]
    public void Dispatched_work_rejects_false_cancelled_and_backward_transitions(HostTaskState state)
    {
        var dispatched = new HostTaskRecord(HostRequest.Create(RequestOrigin.HostSystem), new(2), HostTaskState.DispatchRecorded);
        var action = () => dispatched.Next(state);
        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ResourceAccess.SharedRead, true)]
    [InlineData(ResourceAccess.Exclusive, false)]
    [InlineData(ResourceAccess.Unknown, false)]
    public void Unknown_resources_never_claim_concurrency(ResourceAccess access, bool expected)
    {
        new ResourceDescriptor(new(Guid.NewGuid()), access).AllowsConcurrency.Should().Be(expected);
    }

    [Fact]
    public async Task Unadmitted_store_fails_without_creating_replacement_data()
    {
        var store = new UnavailableHostTaskStore();
        var record = new HostTaskRecord(HostRequest.Create(RequestOrigin.HostSystem), new(1), HostTaskState.IntentRecorded);
        var commit = async () => await store.CommitAsync(record, 0, CancellationToken.None);
        await commit.Should().ThrowAsync<StorageAdmissionException>();
        var read = async () => await store.ReadIncompleteAsync(1, CancellationToken.None);
        await read.Should().ThrowAsync<StorageAdmissionException>();
    }

    [Fact]
    public void Malformed_persisted_states_are_not_defaults()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var state = () => new HostTaskRecord(request, new(1), (HostTaskState)999);
        state.Should().Throw<InvalidDataException>();
        var revision = () => new HostTaskRecord(request, default, HostTaskState.Succeeded);
        revision.Should().Throw<InvalidDataException>();
        var origin = () => new HostRequest(request.RequestId, request.SessionId, request.TaskId, (RequestOrigin)999);
        origin.Should().Throw<ArgumentOutOfRangeException>();
        var resource = () => new ResourceDescriptor(new(Guid.NewGuid()), (ResourceAccess)999);
        resource.Should().Throw<ArgumentOutOfRangeException>();
    }
}
