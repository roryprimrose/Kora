using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("equal-copy")]
    [InlineData("stopped")]
    [InlineData("completed")]
    public async Task LocalContextCreationRequiresExactLiveOriginalSourceAuthority(string state)
    {
        using var fixture = new Fixture();
        using var root = state is "missing" ? null
            : HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        if (state is "stopped")
        {
            root!.Activity!.Stop();
            Activity.Current = root.Activity;
        }
        if (state is "completed") { root!.Complete(HostOperationOutcome.Completed); }
        var source = state switch
        {
            "foreign" => HostRequest.Create(fixture.Request.Origin),
            "equal-copy" => new(fixture.Request.RequestId, fixture.Request.SessionId, fixture.Request.TaskId, fixture.Request.Origin),
            _ => fixture.Request,
        };
        var create = () => fixture.Service.CreateLocalContextSessionAsync(source, static () => true, fixture.Token);

        await create.Should().ThrowAsync<InvalidOperationException>();

        fixture.TaskWrites.Should().BeEmpty();
        if (state is "stopped") { HostActivity.Current.Should().BeNull(); }
        else { HostActivity.Current.Should().BeSameAs(root); }
    }

    [Fact]
    public async Task LocalContextCreationOwnsADistinctControlTaskAndOnlyTheReviewedSessionName()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        fixture.LocalContextCreation = (request, name, eligible, _) =>
        {
            request.SessionId.Should().Be(fixture.Request.SessionId);
            request.RequestId.Should().NotBe(fixture.Request.RequestId);
            request.TaskId.Should().NotBe(fixture.Request.TaskId);
            request.Origin.Should().Be(fixture.Request.Origin);
            HostActivity.RequireCurrent().Request.Should().BeSameAs(request);
            name.Value.Should().Be(LocalContextSessionTarget.FreshSessionName);
            eligible().Should().BeTrue();
            return ValueTask.FromResult(new SessionWorkspaceEntry(fixture.Session, new(request.SessionId, new(1), name)));
        };

        var created = await fixture.Service.CreateLocalContextSessionAsync(fixture.Request, static () => true, fixture.Token);

        created.Authority.Should().Be(fixture.Session);
        fixture.TaskWrites.Select(task => task.State).Should()
            .Equal(HostTaskState.IntentRecorded, HostTaskState.Succeeded);
        fixture.TaskWrites[1].Request.Should().BeSameAs(fixture.TaskWrites[0].Request);
        fixture.TaskWrites.Should().NotContain(task => task.Request.TaskId == fixture.Request.TaskId);
        HostActivity.Current.Should().BeSameAs(root);
    }

    [Fact]
    public async Task LocalContextCreationCancellationAfterSessionCommitClosesTheCreationTaskAsUnknown()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = new CancellationTokenSource();
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<SessionWorkspaceEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        SessionWorkspaceEntry? persisted = null;
        fixture.LocalContextCreation = async (request, name, eligible, token) =>
        {
            eligible().Should().BeTrue();
            persisted = new(fixture.Session, new(request.SessionId, new(1), name));
            committed.TrySetResult();
            return await pending.Task.WaitAsync(token);
        };
        var creation = fixture.Service.CreateLocalContextSessionAsync(fixture.Request, static () => true, cancellation.Token);
        await committed.Task.WaitAsync(fixture.Token);

        await cancellation.CancelAsync();
        var observe = () => creation;
        await observe.Should().ThrowAsync<OperationCanceledException>();

        persisted.Should().NotBeNull();
        persisted!.Authority.IsActive.Should().BeTrue();
        fixture.TaskWrites.Select(task => task.State).Should()
            .Equal(HostTaskState.IntentRecorded, HostTaskState.Unknown);
        fixture.TaskWrites[1].Request.Should().BeSameAs(fixture.TaskWrites[0].Request);
        fixture.TaskWrites.Should().NotContain(task => task.State == HostTaskState.Succeeded);
        HostActivity.Current.Should().BeSameAs(root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalContextCreationFailureBeforeIntentNeverManufacturesATerminalReceipt(bool cancel)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        Exception failure = cancel ? new OperationCanceledException(fixture.Token) : new IOException("intent unavailable");
        fixture.BeforeIntent = _ => throw failure;
        var create = () => fixture.Service.CreateLocalContextSessionAsync(fixture.Request, static () => true, fixture.Token);

        var thrown = await create.Should().ThrowAsync<Exception>();

        thrown.Which.Should().BeSameAs(failure);
        fixture.TaskWrites.Should().BeEmpty();
        HostActivity.Current.Should().BeSameAs(root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalContextCreationTerminalFailureIsNotRetriedOrOverwrittenAsUnknown(bool cancel)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var attempts = 0;
        Exception failure = cancel ? new OperationCanceledException(fixture.Token) : new IOException("terminal unavailable");
        fixture.BeforeTaskWrite = record =>
        {
            if (!record.IsTerminal) { return; }
            attempts++;
            throw failure;
        };
        var create = () => fixture.Service.CreateLocalContextSessionAsync(fixture.Request, static () => true, fixture.Token);

        var thrown = await create.Should().ThrowAsync<Exception>();

        thrown.Which.Should().BeSameAs(failure);
        attempts.Should().Be(1);
        fixture.TaskWrites.Should().ContainSingle().Which.State.Should().Be(HostTaskState.IntentRecorded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLocalContextCreationCleanupRemainsUnconfirmedWithoutRetry(bool cancelled)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        fixture.LocalContextCreation = (_, _, _, _) => throw (cancelled
            ? new OperationCanceledException() : new IOException("creation failed"));
        var attempts = 0;
        fixture.BeforeTaskWrite = record =>
        {
            if (record.State != HostTaskState.Unknown) { return; }
            attempts++;
            throw new IOException("terminal cleanup unavailable");
        };
        var create = () => fixture.Service.CreateLocalContextSessionAsync(fixture.Request, static () => true, fixture.Token);
        await create.Should().ThrowAsync<IOException>().WithMessage("terminal cleanup unavailable");
        attempts.Should().Be(1);
        fixture.TaskWrites.Should().ContainSingle().Which.State.Should().Be(HostTaskState.IntentRecorded);
    }
}
