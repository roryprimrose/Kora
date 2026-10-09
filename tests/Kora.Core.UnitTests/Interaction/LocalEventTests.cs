using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class LocalEventTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 4, 1, 55, 0, TimeSpan.Zero);
    internal static LocalEvent Event(LocalEventType type = LocalEventType.Queued)
    {
        var session = new HostId<SessionIdentity>(Guid.Parse("706b807c-f1ea-4d54-a0c4-f40239156f94"));
        var task = new HostId<TaskIdentity>(Guid.Parse("d4b29fd1-f762-4b9f-a295-263726697d03"));
        var source = type == LocalEventType.UserAttention ? LocalEventSource.LocalVersionQuestion
            : type == LocalEventType.MaintenanceAvailable ? LocalEventSource.CachedMaintenance : LocalEventSource.LocalVersionQueue;
        return new(LocalEvent.Identity(source, session.Value, task.Value), 1, source, type, session,
            source == LocalEventSource.CachedMaintenance ? null : task, task.Value, 1, 1, 0,
            source == LocalEventSource.CachedMaintenance ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            source == LocalEventSource.LocalVersionQueue ? SessionQueueEligibility.Ready : null, Now, Now.AddMinutes(30));
    }

    [Theory]
    [InlineData(LocalEventType.Queued, LocalEventCategory.Work, 1)]
    [InlineData(LocalEventType.Current, LocalEventCategory.Work, 1)]
    [InlineData(LocalEventType.Completed, LocalEventCategory.Work, 1)]
    [InlineData(LocalEventType.Failed, LocalEventCategory.Failure, 3)]
    [InlineData(LocalEventType.Blocked, LocalEventCategory.Failure, 3)]
    [InlineData(LocalEventType.Unknown, LocalEventCategory.Failure, 3)]
    [InlineData(LocalEventType.UserAttention, LocalEventCategory.Attention, 2)]
    [InlineData(LocalEventType.MaintenanceAvailable, LocalEventCategory.Maintenance, 0)]
    public void Host_types_have_stable_content_free_identity_priority_and_bounded_fixed_summaries(
        LocalEventType type, LocalEventCategory category, int priority)
    {
        var item = Event(type);
        item.Validate();
        item.Category.Should().Be(category);
        item.Priority.Should().Be(priority);
        item.Summary.Length.Should().BeInRange(1, 256);
        item.SameSource(item with { Revision = 999, ObservedAt = Now.AddMinutes(1) }).Should().BeTrue();
        new LocalEventView(item, LocalEventReason.Eligible, null).DisplaySummary.Should().Be(item.Summary);
        new LocalEventView(item, LocalEventReason.CategoryLimit, null).DisplaySummary.Should().NotBe(item.Summary);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("source")]
    [InlineData("origin")]
    [InlineData("reason")]
    [InlineData("subject")]
    [InlineData("revision")]
    [InlineData("generation")]
    [InlineData("source-revision")]
    [InlineData("related-revision")]
    [InlineData("observed-offset")]
    [InlineData("expiry-offset")]
    [InlineData("expiry")]
    [InlineData("lifetime")]
    [InlineData("task-missing")]
    [InlineData("queue-attention")]
    [InlineData("queue-maintenance")]
    [InlineData("queue-reason")]
    [InlineData("voice-origin")]
    public void Malformed_event_metadata_cannot_become_host_authority(string field)
    {
        var item = field switch
        {
            "id" => Event() with { Id = Guid.NewGuid() },
            "type" => Event() with { Type = (LocalEventType)99 },
            "source" => Event() with { Source = (LocalEventSource)99 },
            "origin" => Event() with { Origin = (RequestOrigin)99 },
            "reason" => Event() with { QueueReason = (SessionQueueEligibility)99 },
            "subject" => Event() with { SubjectId = Guid.Empty },
            "revision" => Event() with { Revision = 0 },
            "generation" => Event() with { Generation = 0 },
            "source-revision" => Event() with { SourceRevision = 0 },
            "related-revision" => Event() with { RelatedRevision = -1 },
            "observed-offset" => Event() with { ObservedAt = Now.ToOffset(TimeSpan.FromHours(1)) },
            "expiry-offset" => Event() with { ExpiresAt = Now.AddMinutes(30).ToOffset(TimeSpan.FromHours(1)) },
            "expiry" => Event() with { ExpiresAt = Now },
            "lifetime" => Event() with { ExpiresAt = Now.AddHours(7) },
            "task-missing" => Event() with { TaskId = null },
            "queue-attention" => Event() with { Type = LocalEventType.UserAttention },
            "queue-maintenance" => Event() with { Type = LocalEventType.MaintenanceAvailable },
            "queue-reason" => Event() with { QueueReason = null },
            _ => Event() with { Origin = RequestOrigin.HostSystem },
        };
        item.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(LocalEventType.UserAttention)]
    [InlineData(LocalEventType.MaintenanceAvailable)]
    public void Source_kinds_cannot_impersonate_each_other(LocalEventType type)
    {
        var item = Event(type);
        (item with { Type = LocalEventType.Queued }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        (item with { QueueReason = SessionQueueEligibility.Current }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        if (type == LocalEventType.MaintenanceAvailable)
        {
            (item with { TaskId = new(Guid.NewGuid()) }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
            (item with { Origin = RequestOrigin.LocalUi }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void Every_source_fingerprint_field_is_exact_but_broker_revision_and_observation_time_are_not_authority()
    {
        var item = Event();
        var changes = new[]
        {
            item with { Id = Guid.NewGuid() }, item with { Source = LocalEventSource.LocalVersionQuestion },
            item with { Type = LocalEventType.Blocked }, item with { SessionId = new(Guid.NewGuid()) },
            item with { TaskId = new(Guid.NewGuid()) }, item with { TaskId = null }, item with { SubjectId = Guid.NewGuid() },
            item with { Generation = 2 }, item with { SourceRevision = 2 }, item with { RelatedRevision = 2 },
            item with { Origin = RequestOrigin.ActivatedVoice }, item with { QueueReason = SessionQueueEligibility.GlobalCapacity },
            item with { ExpiresAt = item.ExpiresAt.AddSeconds(1) },
        };
        changes.Should().OnlyContain(other => !item.SameSource(other));
        (item with { Origin = RequestOrigin.ActivatedVoice }).Validate();
    }
}
