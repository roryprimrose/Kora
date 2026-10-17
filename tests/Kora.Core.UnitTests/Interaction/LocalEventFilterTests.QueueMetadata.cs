using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Interaction;

public sealed partial class LocalEventFilterTests
{
    private static LocalEventView[] QueueMetadataRows() =>
    [
        new(LocalEventTests.Event(), LocalEventReason.Eligible, null),
        new(LocalEventTests.Event(LocalEventType.UserAttention), LocalEventReason.Eligible, null),
        new(LocalEventTests.Event(LocalEventType.MaintenanceAvailable), LocalEventReason.Eligible, null),
        .. Enum.GetValues<SessionQueueReadProfile>().Where(profile => profile != SessionQueueReadProfile.Capability)
            .Select(profile => new LocalEventView(LocalEventTests.Event() with { QueueProfile = profile }, LocalEventReason.Eligible, null)),
        .. ReadOnlyCapabilityCatalog.Descriptors.Select(target => new LocalEventView(LocalEventTests.Event() with
        { QueueProfile = SessionQueueReadProfile.Capability, QueueTarget = new(target.Id) }, LocalEventReason.Eligible, null)),
    ];

    [Fact]
    public void ProfilePresenceAndEveryCanonicalProfileTargetComposeWithAllExistingPredicates()
    {
        var rows = QueueMetadataRows();
        LocalEventQueueProfileFilter[] profiles = [new(), new(LocalEventQueueProfileMatch.Absent),
            .. Enum.GetValues<SessionQueueReadProfile>().Select(value => new LocalEventQueueProfileFilter(LocalEventQueueProfileMatch.Exact, value))];
        SessionQueueCapabilityTarget?[] targets = [null, .. ReadOnlyCapabilityCatalog.Descriptors.Select(value => new SessionQueueCapabilityTarget(value.Id))];
        foreach (var profile in profiles)
        foreach (var target in targets)
        {
            var filter = new LocalEventFilter(QueueProfile: profile, QueueTarget: target);
            filter.IsValid.Should().BeTrue();
            rows.Where(filter.Matches).Should().Equal(rows.Where(row =>
                (profile.Match == LocalEventQueueProfileMatch.Any
                    || (profile.Match == LocalEventQueueProfileMatch.Absent ? row.Event.QueueProfile is null : row.Event.QueueProfile == profile.Value))
                && (target is null || row.Event.QueueTarget == target)));
            foreach (var row in rows)
            {
                var complete = filter with
                {
                    Category = row.Event.Category, Reason = row.Reason, Source = row.Event.Source,
                    Type = row.Event.Type, Origin = row.Event.Origin,
                    QueueReason = row.Event.QueueReason is { } reason ? new(LocalEventQueueReasonMatch.Exact, reason) : new(LocalEventQueueReasonMatch.None),
                    SessionId = row.Event.SessionId.Value.ToString("D"), TaskId = row.Event.TaskId?.Value.ToString("D") ?? "",
                    SubjectId = row.Event.SubjectId.ToString("D"),
                    MinimumRevision = "1", MaximumRevision = "1", MinimumGeneration = "1", MaximumGeneration = "1",
                    ObservedFromUtc = row.Event.ObservedAt.ToString("O", CultureInfo.InvariantCulture),
                    ObservedUntilUtc = row.Event.ObservedAt.ToString("O", CultureInfo.InvariantCulture),
                    ExpiresFromUtc = row.Event.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
                    ExpiresUntilUtc = row.Event.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
                };
                complete.IsValid.Should().BeTrue();
                complete.Matches(row).Should().Be(filter.Matches(row));
                complete.Matches(row with { Event = row.Event with { Revision = 2 } }).Should().BeFalse();
                complete.Matches(row with { Event = row.Event with { Generation = 2 } }).Should().BeFalse();
                complete.Matches(row with { Reason = LocalEventReason.PresentedNoReplay }).Should().BeFalse();
            }
        }
        rows.Where(new LocalEventFilter(QueueProfile: new(LocalEventQueueProfileMatch.Absent)).Matches)
            .Should().HaveCount(3, "legacy queue, question and maintenance absence is explicit complete-row metadata");
        var runtime = rows.Single(row => row.Event.QueueProfile == SessionQueueReadProfile.RuntimeStatus);
        new LocalEventFilter(QueueTarget: new(ReadOnlyCapabilityCatalog.RuntimeStatus)).Matches(runtime).Should().BeFalse();
    }

    [Fact]
    public void UnknownPartialOrRepairedProfileTargetPredicatesFailClosed()
    {
        var row = QueueMetadataRows()[0];
        LocalEventFilter[] invalid =
        [
            new(QueueProfile: new((LocalEventQueueProfileMatch)99)),
            new(QueueProfile: new((LocalEventQueueProfileMatch)(-1))),
            new(QueueProfile: new(LocalEventQueueProfileMatch.Any, SessionQueueReadProfile.Version)),
            new(QueueProfile: new(LocalEventQueueProfileMatch.Absent, SessionQueueReadProfile.Version)),
            new(QueueProfile: new(LocalEventQueueProfileMatch.Exact)),
            new(QueueProfile: new(LocalEventQueueProfileMatch.Exact, (SessionQueueReadProfile)99)),
            new(QueueProfile: new(LocalEventQueueProfileMatch.Exact, (SessionQueueReadProfile)(-1))),
            .. new[] { null, "", " ", "APPLICATION.GET_VERSION", "application.get_version ", "version", "local.inference", "unknown" }
                .Select(id => new LocalEventFilter(QueueTarget: new(id!))),
        ];
        foreach (var filter in invalid)
        {
            filter.IsValid.Should().BeFalse();
            filter.Matches(row).Should().BeFalse();
            filter.Invoking(value => _ = value.Digest).Should().Throw<InvalidDataException>();
            filter.Invoking(value => value.Matches(row with { Event = row.Event with { Revision = 0 } }))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void CompleteMalformedProfilesAndTargetsAreValidatedEvenForAbsentInvalidOrNonmatchingFilters()
    {
        var item = LocalEventTests.Event();
        LocalEvent[] malformed =
        [
            item with { QueueTarget = new(ReadOnlyCapabilityCatalog.List) },
            item with { QueueProfile = (SessionQueueReadProfile)99 },
            item with { QueueProfile = SessionQueueReadProfile.Capability },
            item with { QueueProfile = SessionQueueReadProfile.Version, QueueTarget = new(ReadOnlyCapabilityCatalog.List) },
            item with { QueueProfile = SessionQueueReadProfile.RuntimeStatus, QueueTarget = new(ReadOnlyCapabilityCatalog.RuntimeStatus) },
            LocalEventTests.Event(LocalEventType.UserAttention) with { QueueProfile = SessionQueueReadProfile.Version },
            LocalEventTests.Event(LocalEventType.MaintenanceAvailable) with { QueueProfile = SessionQueueReadProfile.Version },
            .. new[] { null, "", "unknown", "CAPABILITIES.LIST", "capabilities.list " }.Select(id =>
                item with { QueueProfile = SessionQueueReadProfile.Capability, QueueTarget = new(id!) }),
        ];
        foreach (var invalid in malformed)
        foreach (var filter in new LocalEventFilter[]
        {
            new(), new(QueueProfile: new(LocalEventQueueProfileMatch.Absent)),
            new(QueueProfile: new(LocalEventQueueProfileMatch.Exact, SessionQueueReadProfile.Readiness)),
            new(QueueTarget: new(ReadOnlyCapabilityCatalog.Version)),
            new(QueueTarget: new("unknown")),
            new(Category: LocalEventCategory.Maintenance),
        })
        {
            filter.Invoking(value => value.Matches(new(invalid, LocalEventReason.Eligible, null)))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void VersionedContinuationDigestBindsEveryPredicateAndDistinguishesAnyAbsentAndEveryExactDescriptor()
    {
        var baseline = new LocalEventFilter();
        var id = LocalEventTests.Event().SessionId.Value.ToString("D");
        var utc = LocalEventTests.Now.ToString("O", CultureInfo.InvariantCulture);
        LocalEventFilter[] variants =
        [
            baseline, baseline with { Category = LocalEventCategory.Work }, baseline with { Reason = LocalEventReason.Eligible },
            baseline with { Source = LocalEventSource.LocalVersionQueue }, baseline with { Type = LocalEventType.Queued },
            baseline with { Origin = RequestOrigin.LocalUi },
            baseline with { QueueReason = new(LocalEventQueueReasonMatch.None) },
            baseline with { QueueReason = new(LocalEventQueueReasonMatch.Exact, SessionQueueEligibility.Ready) },
            baseline with { SessionId = id }, baseline with { TaskId = id }, baseline with { SubjectId = id },
            baseline with { MinimumRevision = "1" }, baseline with { MaximumRevision = "1" },
            baseline with { MinimumGeneration = "1" }, baseline with { MaximumGeneration = "1" },
            baseline with { ObservedFromUtc = utc }, baseline with { ObservedUntilUtc = utc },
            baseline with { ExpiresFromUtc = utc }, baseline with { ExpiresUntilUtc = utc },
            baseline with { QueueProfile = new(LocalEventQueueProfileMatch.Absent) },
            .. Enum.GetValues<SessionQueueReadProfile>().Select(profile =>
                baseline with { QueueProfile = new(LocalEventQueueProfileMatch.Exact, profile) }),
            .. ReadOnlyCapabilityCatalog.Descriptors.Select(target => baseline with { QueueTarget = new(target.Id) }),
        ];
        variants.Select(filter => filter.Digest).Should().OnlyHaveUniqueItems();
        foreach (var filter in variants)
        {
            filter.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
            (filter with { }).Digest.Should().Be(filter.Digest);
        }
    }
}
