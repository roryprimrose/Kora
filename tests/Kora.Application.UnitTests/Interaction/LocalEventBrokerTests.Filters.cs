using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class LocalEventBrokerTests
{
    [Fact]
    public async Task LoadedOriginAndQueueReasonFiltersDoNotReadCommitRenewOrConfuseQuestionAndMaintenanceWithQueueSuccess()
    {
        using var f = new Fixture();
        var reads = 0;
        f.BeforeRead = _ => { reads++; return Task.CompletedTask; };
        f.Events = [f.Event(LocalEventType.UserAttention), f.Event(LocalEventType.MaintenanceAvailable),
            f.Event() with { Origin = RequestOrigin.ActivatedVoice, QueueReason = SessionQueueEligibility.DispatchPaused }];
        await using var broker = f.Create();
        var snapshot = await f.Observe(broker);
        var saved = f.Saved;
        var audits = f.Audits.ToArray();
        RequestOrigin?[] origins = [null, .. Enum.GetValues<RequestOrigin>().Select(value => (RequestOrigin?)value)];
        foreach (var origin in origins)
        foreach (var match in Enum.GetValues<LocalEventQueueReasonMatch>())
        {
            var choices = match == LocalEventQueueReasonMatch.Exact
                ? Enum.GetValues<SessionQueueEligibility>().Select(value => new LocalEventQueueReasonFilter(match, value))
                : [new LocalEventQueueReasonFilter(match)];
            foreach (var queue in choices)
            {
                var expected = snapshot.Events.Where(row => (origin is null || row.Event.Origin == origin)
                    && (match == LocalEventQueueReasonMatch.Any || (match == LocalEventQueueReasonMatch.None
                        ? row.Event.QueueReason is null : row.Event.QueueReason == queue.Value)));
                snapshot.Events.Where(new LocalEventFilter(Origin: origin, QueueReason: queue).Matches).Should().Equal(expected);
                broker.IsCurrent(snapshot).Should().BeTrue();
            }
        }
        snapshot.Events.Where(new LocalEventFilter(QueueReason: new(LocalEventQueueReasonMatch.None)).Matches)
            .Should().OnlyContain(row => row.Event.Source != LocalEventSource.LocalVersionQueue);
        snapshot.Events.Where(new LocalEventFilter(Origin: RequestOrigin.HostSystem,
            QueueReason: new(LocalEventQueueReasonMatch.Exact, SessionQueueEligibility.Completed)).Matches).Should().BeEmpty();
        reads.Should().Be(1);
        f.Saves.Should().Be(1);
        f.Saved.Should().BeSameAs(saved);
        f.Audits.Should().Equal(audits);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("queue")]
    [InlineData("revision")]
    [InlineData("expiry")]
    public async Task FilteredExactEventsCannotSurviveSourceIdentityRevisionOrDeadlineChanges(string change)
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        var snapshot = await f.Observe(broker);
        var row = snapshot.Events.Single();
        var filter = new LocalEventFilter(Origin: row.Event.Origin,
            QueueReason: new(LocalEventQueueReasonMatch.Exact, row.Event.QueueReason));
        filter.Matches(row).Should().BeTrue();
        f.Events = [change switch
        {
            "origin" => row.Event with { Origin = RequestOrigin.ActivatedVoice },
            "queue" => row.Event with { QueueReason = SessionQueueEligibility.DispatchPaused },
            "revision" => row.Event with { SourceRevision = row.Event.SourceRevision + 1 },
            _ => row.Event with { ExpiresAt = row.Event.ExpiresAt.AddMinutes(-1) },
        }];
        var stale = () => broker.ExecuteAsync(new(LocalEventOperation.Review, row.Event.Id, row.Event.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        (await stale()).Reason.Should().Be(LocalEventReason.SourceUnavailable);
        var current = await f.Observe(broker);
        current.Events.Single().Event.Revision.Should().Be(row.Event.Revision + 1);
        current.Events.Single().Event.ExpiresAt.Should().Be(f.Events.Single().ExpiresAt);
        if (change is "origin" or "queue") { current.Events.Where(filter.Matches).Should().BeEmpty(); }
        f.Time.Now = current.Events.Single().Event.ExpiresAt;
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.Expired);
        var expired = await broker.ExecuteAsync(new(LocalEventOperation.Review,
            current.Events.Single().Event.Id, current.Events.Single().Event.Revision), RequestOrigin.LocalUi, () => true, f.Token);
        expired.Reason.Should().Be(LocalEventReason.Expired);
    }
}
