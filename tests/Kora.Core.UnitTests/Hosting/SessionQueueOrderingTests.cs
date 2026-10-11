using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Hosting;

public sealed class SessionQueueOrderingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Run = Guid.NewGuid();
    private static readonly HostId<SessionIdentity> Session = new(Guid.NewGuid());

    [Fact]
    public void ExplicitOrderChoosesPendingHeadWithoutChangingArrivalFairnessOrAnyEntryPayload()
    {
        var first = Entry(1);
        var other = Entry(2) with { Request = new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi) };
        var last = Entry(3);
        var order = Order([last.Request.TaskId, first.Request.TaskId]);
        order.Validate([first, other, last]);
        SessionQueueOrdering.Pending([first, other, last], Session).Should().Equal(first, last);
        SessionQueueOrdering.Pending([first, other, last], Session, [order]).Should().Equal(last, first);
        SessionQueueOrdering.Head([], Session).Should().BeNull();
        SessionQueueOrdering.Key(other, [order]).Should().Be(2);
        SessionQueueOrdering.Key(first with { State = SessionQueueState.Removed }, [order]).Should().Be(2);
        SessionQueueOrdering.Key(Entry(4) with { State = SessionQueueState.Succeeded }, [order]).Should().Be(4);
        SessionQueueOrdering.Key(first with { RunId = Guid.NewGuid() }, [order]).Should().Be(1);
        SessionQueueOrdering.Key(first with { Generation = new(2) }, [order]).Should().Be(1);
        var states = new Dictionary<HostId<TaskIdentity>, HostTaskState>();
        SessionQueuePolicy.SelectReady([first, other, last], states, Run, Now, 1, new(), orders: [order]).Should().Be(last,
            "reviewed head arrival must not change the oldest-pending session fairness tie");
        SessionQueuePolicy.Eligibility(first, [first, last], states, Run, Now, 1, new(), orders: [order])
            .Should().Be(SessionQueueEligibility.EarlierPendingEntry);
        SessionQueuePolicy.Eligibility(last, [first, last], states, Run, Now, 1, new(), orders: [order])
            .Should().Be(SessionQueueEligibility.Ready);
        SessionQueuePolicy.SelectReady([first, last with { State = SessionQueueState.Succeeded, DispatchOrder = 10 }, other],
            states, Run, Now, 1, new()).Should().Be(other, "last admitted session still yields its turn");
        var blocked = last with { Dependency = other.Request.TaskId };
        SessionQueuePolicy.Eligibility(blocked, [first, blocked, other], states, Run, Now, 1, new(), orders: [order])
            .Should().Be(SessionQueueEligibility.DependencyNotSucceeded);
        first.Position.Should().Be(1);
        last.Position.Should().Be(3);
        last.EnqueuedAt.Should().Be(Now);
        last.ExpiresAt.Should().Be(Now.AddMinutes(30));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdjacentMoveRejectsChangedOrIneligiblePairAndDependencyInversion(bool up)
    {
        var first = Entry(1);
        var second = Entry(2);
        var selected = up ? second : first;
        var neighbor = up ? first : second;
        SessionQueueOrdering.Refusal([first, second], selected, neighbor, up, Run, new(1), 1, Now).Should().BeNull();
        SessionQueueOrdering.Refusal([selected, selected], selected, selected, up, Run, new(1), 1, Now).Should().NotBeNull();
        var dependent = second with { Dependency = first.Request.TaskId };
        SessionQueueOrdering.Refusal([first, dependent], up ? dependent : first, up ? first : dependent,
            up, Run, new(1), 1, Now).Should().Contain("prerequisite");
        foreach (var state in Enum.GetValues<SessionQueueState>().Where(state => state != SessionQueueState.Pending))
        {
            SessionQueueOrdering.Refusal([first, second], selected with { State = state }, neighbor,
                up, Run, new(1), 1, Now).Should().Contain(state == SessionQueueState.Expired ? "expired" : "current-run pending");
            SessionQueueOrdering.Refusal([first, second], selected, neighbor with { State = state },
                up, Run, new(1), 1, Now).Should().Contain(state == SessionQueueState.Expired ? "expired" : "current-run pending");
        }
        foreach (var malformed in new[]
        {
            selected with { RunId = Guid.NewGuid() }, selected with { Generation = new(2) },
            selected with { Request = new(selected.Request.RequestId, new(Guid.NewGuid()), selected.Request.TaskId, RequestOrigin.LocalUi) },
        })
        {
            SessionQueueOrdering.Refusal([first, second], malformed, neighbor, up, Run, new(1), 1, Now).Should().NotBeNull();
        }
        SessionQueueOrdering.Refusal([first, second], selected, neighbor with { RunId = Guid.NewGuid() }, up, Run, new(1), 1, Now).Should().NotBeNull();
        SessionQueueOrdering.Refusal([first, second], selected, neighbor with { Generation = new(2) }, up, Run, new(1), 1, Now).Should().NotBeNull();
        SessionQueueOrdering.Refusal([first, second], selected, neighbor, up, Run, new(1), 1, selected.ExpiresAt).Should().Contain("expired");
        SessionQueueOrdering.Refusal([first, second], selected with { ExpiresAt = Now.AddMinutes(60) }, neighbor,
            up, Run, new(1), 1, neighbor.ExpiresAt).Should().Contain("expired");
        SessionQueueOrdering.Refusal([first, second], selected with { AdmissionRevision = 2 }, neighbor,
            up, Run, new(1), 1, Now).Should().Contain("Private admission");
        SessionQueueOrdering.Refusal([first, second], selected, neighbor with { AdmissionRevision = 2 },
            up, Run, new(1), 1, Now).Should().Contain("Private admission");
        SessionQueueOrdering.Refusal([], selected, neighbor, up, Run, new(1), 1, Now).Should().Contain("adjacent");
        SessionQueueOrdering.Refusal([selected], selected, neighbor, up, Run, new(1), 1, Now).Should().Contain("adjacent");
        SessionQueueOrdering.Refusal([first, Entry(3), second], selected, neighbor, up, Run, new(1), 1, Now).Should().Contain("adjacent");
    }

    [Fact]
    public void StrictLineageRejectsUnknownMalformedDuplicateCycleCrossSessionAndOutOfSetWithoutDefaults()
    {
        var first = Entry(1);
        var second = Entry(2);
        var order = Order([first.Request.TaskId, second.Request.TaskId]);
        order.Validate([first, second]);
        Order([]).Validate([]);
        foreach (var malformed in new[]
        {
            order with { Version = 0 }, order with { Version = 2 }, order with { Id = Guid.Empty },
            order with { SessionId = default }, order with { Generation = default }, order with { RunId = Guid.Empty },
            order with { Revision = 0 }, order with { Tasks = default },
            order with { Tasks = [first.Request.TaskId, first.Request.TaskId] },
            order with { Tasks = [first.Request.TaskId, default] },
            order with { Tasks = [.. Enumerable.Range(0, 11).Select(_ => new HostId<TaskIdentity>(Guid.NewGuid()))] },
            order with { Tasks = [first.Request.TaskId] }, order with { Tasks = [first.Request.TaskId, new(Guid.NewGuid())] },
            order with { SessionId = new(Guid.NewGuid()) }, order with { RunId = Guid.NewGuid() },
            order with { Generation = new(2) },
        })
        {
            malformed.Invoking(item => item.Validate([first, second])).Should().Throw<InvalidDataException>();
        }
        order.Invoking(item => item.Validate([first with { Dependency = second.Request.TaskId }, second]))
            .Should().Throw<InvalidDataException>();
        order.Invoking(item => item.Validate([first with { Dependency = second.Request.TaskId }, second with { Dependency = first.Request.TaskId }]))
            .Should().Throw<InvalidDataException>();
        order.Validate([first, second with { Dependency = first.Request.TaskId }]);
        order.Validate([first with { Dependency = new(Guid.NewGuid()) }, second]);
    }

    private static SessionQueueOrder Order(ImmutableArray<HostId<TaskIdentity>> tasks) => new(1, Guid.NewGuid(), Session, new(1), Run, 1, tasks);
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CommittedPairReceiptRetainsExactReviewAndRejectsPartialOrIneligibleLineage(bool up)
    {
        var first = Entry(1);
        var second = Entry(2);
        var snapshot = new SessionQueueSnapshot(Session, new(1), 9, [first, second])
            { DispatchControl = new(Run, new(1), 2, 1, false, false) };
        var review = new SessionQueueMoveReview(Guid.NewGuid(), snapshot, up ? second : first, up ? first : second, up, 1, Now);
        var receipt = SessionQueueMoveReceipt.FromReview(review);
        receipt.Validate();
        receipt.ReviewId.Should().Be(review.Id);
        receipt.Selected.Should().Be(review.Selected);
        receipt.Neighbor.Should().Be(review.Neighbor);
        receipt.QueueRevision.Should().Be(9);
        receipt.ControlRevision.Should().Be(2);
        receipt.Up.Should().Be(up);
        foreach (var malformed in new[]
        {
            receipt with { ReviewId = Guid.Empty }, receipt with { QueueRevision = -1 }, receipt with { ControlRevision = -1 },
            receipt with { AdmissionRevision = -1 }, receipt with { ObservedAt = Now.ToOffset(TimeSpan.FromHours(1)) },
            receipt with { Selected = null! }, receipt with { Neighbor = null! },
            receipt with { RunId = Guid.Empty }, receipt with { Selected = receipt.Selected with { State = SessionQueueState.Succeeded } },
            receipt with { Neighbor = receipt.Selected },
        })
        {
            malformed.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
        }
    }

    private static SessionQueueEntry Entry(long position) => new(new(new(Guid.NewGuid()), Session, new(Guid.NewGuid()), RequestOrigin.LocalUi),
        new(1), new(1), position, SessionQueueState.Pending, Run, 1, Now, Now.AddMinutes(30));
}
