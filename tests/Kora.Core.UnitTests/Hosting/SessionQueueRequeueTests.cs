using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Hosting;

public sealed class SessionQueueRequeueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiryIsExactStoredBoundaryAndEveryFreshReceiptFieldIsBound()
    {
        var original = Entry();
        var task = new HostTaskRecord(original.Request, new(1), HostTaskState.IntentRecorded);
        SessionQueueRequeue.RequireExpired(original, task, original.RunId, original.Generation, 1, Now);
        var early = () => SessionQueueRequeue.RequireExpired(original, task, original.RunId, original.Generation, 1, Now.AddTicks(-1));
        early.Should().Throw<InvalidOperationException>();
        foreach (var invalid in new[]
        {
            original with { State = SessionQueueState.Expired }, original with { DispatchOrder = 1 },
            original with { RunId = Guid.NewGuid() }, original with { Generation = new(2) },
            original with { AdmissionRevision = 2 },
            original with { EnqueuedAt = original.EnqueuedAt.ToOffset(TimeSpan.FromHours(1)) },
            original with { ExpiresAt = original.ExpiresAt.ToOffset(TimeSpan.FromHours(1)) },
        })
        {
            var act = () => SessionQueueRequeue.RequireExpired(invalid, task, original.RunId, original.Generation, 1, Now);
            act.Should().Throw<InvalidOperationException>();
        }
        foreach (var invalid in new[]
        {
            new HostTaskRecord(new(task.Request.RequestId, task.Request.SessionId, new(Guid.NewGuid()), task.Request.Origin), new(1), task.State),
            new HostTaskRecord(task.Request, new(1), HostTaskState.Interrupted),
        })
        {
            var act = () => SessionQueueRequeue.RequireExpired(original, invalid, original.RunId, original.Generation, 1, Now);
            act.Should().Throw<InvalidOperationException>();
        }
        var fresh = original with { Request = new(new(Guid.NewGuid()), original.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            Position = 3, EnqueuedAt = Now, ExpiresAt = Now.AddMinutes(30), Dependency = null };
        var receipt = new SessionQueueRequeueReceipt(Guid.NewGuid(), 1, new(original.RunId, new(1), 0, 0, false, false),
            original, task, fresh, 0);
        receipt.Validate();
        var prerequisite = new HostId<TaskIdentity>(Guid.NewGuid());
        (receipt with { Original = original with { Dependency = prerequisite },
            Fresh = fresh with { Dependency = prerequisite } }).Validate();
        (receipt with { Original = original with { Dependency = prerequisite } })
            .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        (receipt with { Original = original with { Dependency = prerequisite },
            Fresh = fresh with { Dependency = new(Guid.NewGuid()) } })
            .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        foreach (var invalid in new[]
        {
            receipt with { ReviewId = Guid.Empty }, receipt with { QueueRevision = -1 }, receipt with { Control = null! },
            receipt with { Original = null! }, receipt with { OriginalTask = null! }, receipt with { Fresh = null! },
            receipt with { ConfigurationRevision = -1 },
        })
        {
            invalid.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        }
        foreach (var invalid in new[]
        {
            fresh with { State = SessionQueueState.Removed }, fresh with { DispatchOrder = 1 },
            fresh with { RunId = Guid.NewGuid() }, fresh with { Generation = new(2) },
            fresh with { Request = new(fresh.Request.RequestId, new(Guid.NewGuid()), fresh.Request.TaskId, fresh.Request.Origin) },
            fresh with { Request = new(fresh.Request.RequestId, fresh.Request.SessionId, original.Request.TaskId, fresh.Request.Origin) },
            fresh with { Request = new(original.Request.RequestId, fresh.Request.SessionId, fresh.Request.TaskId, fresh.Request.Origin) },
            fresh with { Dependency = new(Guid.NewGuid()) },
            fresh with { Profile = SessionQueueReadProfile.Readiness, RecordVersion = 4, PendingLifetimeMinutes = 30 },
            fresh with { EnqueuedAt = fresh.EnqueuedAt.ToOffset(TimeSpan.FromHours(1)) },
            fresh with { ExpiresAt = fresh.ExpiresAt.ToOffset(TimeSpan.FromHours(1)) },
            fresh with { Position = 1 }, fresh with { Revision = new(2) },
            fresh with { Result = new(Kora.Core.Tools.CapabilityOutcome.Succeeded, "unexpected") },
            fresh with { ActiveBudgetMinutes = 1 },
        })
        {
            (receipt with { Fresh = invalid }).Invoking(value => value.Validate()).Should().Throw<Exception>();
        }
        (receipt with { Control = receipt.Control with { Held = true } }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    private static SessionQueueEntry Entry() => new(new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
        RequestOrigin.LocalUi), new(1), new(1), 1, SessionQueueState.Pending, Guid.NewGuid(), 1,
        Now.AddMinutes(-30), Now);
}
