using AwesomeAssertions;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task Permission_controls_use_the_real_session_control_owner_and_exact_native_origin()
    {
        using var f = new PermissionFixture();
        f.Service.SupportsSessionPermission.Should().BeTrue();
        SessionPermissionPolicy original;
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        { original = await f.Service.ReadSessionPermissionAsync(f.Session.SessionId, f.Token); }
        f.TaskWrites.Should().BeEmpty();
        var result = await f.Service.SetSessionPermissionAsync(original, SessionPermissionLevel.ApproveAll,
            static () => true, f.Token);
        result.Level.Should().Be(SessionPermissionLevel.ApproveAll);
        result.SessionId.Should().Be(original.SessionId);
        f.TaskWrites.Should().HaveCount(2);
        f.TaskWrites[0].Request.InvocationId.Should().BeNull();
        f.TaskWrites[0].Request.Origin.Should().Be(RequestOrigin.LocalUi);
        f.TaskWrites[1].State.Should().Be(HostTaskState.Succeeded);
        using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        await FluentActions.Awaiting(() => f.Service.SetSessionPermissionAsync(result, SessionPermissionLevel.ReviewAll,
            static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Saved.Should().Be(result);
    }

    [Fact]
    public async Task Missing_permission_storage_and_tool_origin_never_supply_a_policy_default()
    {
        using var f = new Fixture();
        f.Service.SupportsSessionPermission.Should().BeFalse();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await FluentActions.Awaiting(() => f.Service.ReadSessionPermissionAsync(f.Session.SessionId, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var invocation = new HostRequest(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId,
            RequestOrigin.LocalUi, new(Guid.NewGuid()));
        using var tool = HostActivity.BeginRoot(invocation, HostActivityLayer.Application, HostOperation.Tool);
        await FluentActions.Awaiting(() => f.Service.SetSessionPermissionAsync(SessionPermissionPolicy.Default(f.Session),
            SessionPermissionLevel.ApproveAll, static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
    }

    private sealed class PermissionFixture : Fixture, ISessionPermissionStore
    {
        internal SessionPermissionPolicy? Saved { get; private set; }
        public ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            ValueTask.FromResult(Saved ?? SessionPermissionPolicy.Default(Session));

        public ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(HostRequest request, SessionPermissionPolicy expected,
            SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!admitted() || request.SessionId != expected.SessionId || request.Origin != RequestOrigin.LocalUi)
            { throw new InvalidOperationException("Exact private native admission is required."); }
            Saved = expected with { Level = desired, Revision = new(expected.Revision.Value + 1) };
            return ValueTask.FromResult(Saved);
        }
    }
}
