using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Hosting;

public sealed class SessionQueueCancellationTests
{
    private static readonly HostId<SessionIdentity> Session = new(Guid.NewGuid());
    private static readonly Guid Run = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(SessionQueueLimits.MaximumPendingPerSession)]
    public void EntireBoundedMixedProfileSubsetRetainsDispatchOrderAndOriginalExpiredDeadlines(int count)
    {
        var entries = Enumerable.Range(1, count).Select(index => Entry(index, (SessionQueueReadProfile)((index - 1) % 4))).ToArray();
        var snapshot = Snapshot(entries);
        var selection = Selection(entries);
        SessionQueueCancellation.Select(snapshot, selection).Should().Equal(entries);
        SessionQueueCancellation.Select(snapshot with { DispatchControl = snapshot.DispatchControl! with { Held = true, Paused = true } },
            selection).Should().Equal(entries, "management preserves the exact dispatch hold rather than releasing it");
        var expired = snapshot with { Entries = entries.Select(entry => entry with { State = SessionQueueState.Expired }).ToArray() };
        SessionQueueCancellation.Select(expired, selection).Should().Equal(entries, "expiry observations never renew the original pending row");
        SessionQueueCancellation.Select(snapshot with { Entries = [.. entries.Reverse()] }, Selection(entries.Reverse()))
            .Should().Equal(entries.Reverse());
        var review = new SessionQueueCancelReview(Guid.NewGuid(), snapshot, [.. entries], 1, Now);
        var receipt = SessionQueueCancelReceipt.FromReview(review);
        receipt.Validate();
        receipt.Entries.Should().Equal(entries);
        receipt.ReviewId.Should().Be(review.Id);
        receipt.QueueRevision.Should().Be(4);
        receipt.RunId.Should().Be(Run);
        receipt.ControlRevision.Should().Be(0);
        receipt.AdmissionRevision.Should().Be(1);
        receipt.ObservedAt.Should().Be(Now);
    }

    [Fact]
    public void EmptyDuplicateOutOfBoundsMissingControlAndMalformedSelectionsRefuseTheWholeSet()
    {
        var first = Entry(1);
        var second = Entry(2);
        var snapshot = Snapshot([first, second]);
        foreach (var invalid in new ImmutableArray<SessionQueueSelection>[]
        {
            default, [], [new(first.Request.TaskId, first.Revision), new(first.Request.TaskId, first.Revision)],
            [null!], [new(default, new(1))], [new(first.Request.TaskId, default)],
            [.. Enumerable.Range(1, 11).Select(index => new SessionQueueSelection(Entry(index).Request.TaskId, new(1)))],
            Selection([first, Entry(3)]), Selection([second, first]),
        })
        {
            snapshot.Invoking(value => SessionQueueCancellation.Select(value, invalid)).Should().Throw<InvalidOperationException>();
        }
        foreach (var invalid in new[]
        {
            snapshot with { DispatchControl = null },
        })
        {
            invalid.Invoking(value => SessionQueueCancellation.Select(value, Selection([first]))).Should().Throw<InvalidOperationException>();
        }
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("run")]
    [InlineData("revision")]
    [InlineData("state")]
    public void OneWrongMemberInvalidatesAllWithoutSkippingIt(string mismatch)
    {
        var first = Entry(1);
        var second = Entry(2);
        var wrong = mismatch switch
        {
            "session" => second with { Request = new(second.Request.RequestId, new(Guid.NewGuid()), second.Request.TaskId, second.Request.Origin) },
            "generation" => second with { Generation = new(2) },
            "run" => second with { RunId = Guid.NewGuid() },
            "revision" => second with { Revision = new(2) },
            _ => second with { State = SessionQueueState.Unknown },
        };
        Snapshot([first, wrong]).Invoking(value => SessionQueueCancellation.Select(value, Selection([first, second])))
            .Should().Throw<InvalidOperationException>();
        SessionQueueCancellation.Select(Snapshot([Entry(3), first, second]), Selection([first, second])).Should().Equal(first, second);
    }

    [Fact]
    public void RetainedReceiptRejectsEveryPartialForeignOrNonPendingAuthorityShape()
    {
        var first = Entry(1);
        var second = Entry(2);
        var receipt = SessionQueueCancelReceipt.FromReview(new(Guid.NewGuid(), Snapshot([first, second]), [first, second], 1, Now));
        foreach (var invalid in new[]
        {
            receipt with { ReviewId = Guid.Empty }, receipt with { QueueRevision = -1 }, receipt with { RunId = Guid.Empty },
            receipt with { ControlRevision = -1 }, receipt with { AdmissionRevision = -1 },
            receipt with { ObservedAt = Now.ToOffset(TimeSpan.FromHours(1)) },
            receipt with { Entries = default }, receipt with { Entries = [] }, receipt with { Entries = [null!] },
            receipt with { Entries = [first with { Request = null! }] },
            receipt with { Entries = [first, first] },
            receipt with { Entries = [.. Enumerable.Range(1, 11).Select(index => Entry(index))] },
            receipt with { Entries = [first, second with { State = SessionQueueState.Cancelled }] },
            receipt with { Entries = [first, second with { RunId = Guid.NewGuid() }] },
            receipt with { Entries = [first, second with { Request = new(second.Request.RequestId, new(Guid.NewGuid()), second.Request.TaskId, second.Request.Origin) }] },
            receipt with { Entries = [first, second with { Generation = new(2) }] },
        })
        {
            invalid.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        }
    }

    private static SessionQueueSnapshot Snapshot(IReadOnlyList<SessionQueueEntry> entries) =>
        new(Session, new(1), 4, entries) { DispatchControl = new(Run, new(1), 0, 1, false, false) };
    private static ImmutableArray<SessionQueueSelection> Selection(IEnumerable<SessionQueueEntry> entries) =>
        [.. entries.Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision))];
    private static SessionQueueEntry Entry(int position, SessionQueueReadProfile profile = SessionQueueReadProfile.Version) =>
        new(new(new(Guid.NewGuid()), Session, new(Guid.NewGuid()), RequestOrigin.LocalUi), new(1), new(1), position,
            SessionQueueState.Pending, Run, 1, Now, Now.AddMinutes(30),
            RecordVersion: profile == SessionQueueReadProfile.Version ? null : 4,
            PendingLifetimeMinutes: profile == SessionQueueReadProfile.Version ? null : 30,
            Profile: profile == SessionQueueReadProfile.Version ? null : profile);
}
