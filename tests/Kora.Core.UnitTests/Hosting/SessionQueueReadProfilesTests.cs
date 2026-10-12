using System.Text.Json;

using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Hosting;

public sealed class SessionQueueReadProfilesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SessionQueueReadProfile.Version, "version", ReadOnlyCapabilityCatalog.Version)]
    [InlineData(SessionQueueReadProfile.Capabilities, ReadOnlyCapabilityCatalog.List, ReadOnlyCapabilityCatalog.List)]
    [InlineData(SessionQueueReadProfile.Readiness, ReadOnlyCapabilityCatalog.Readiness, ReadOnlyCapabilityCatalog.Readiness)]
    [InlineData(SessionQueueReadProfile.Runtimes, ReadOnlyCapabilityCatalog.RuntimeList, ReadOnlyCapabilityCatalog.RuntimeList)]
    public void Exact_typed_and_current_name_grammar_select_only_a_closed_no_argument_profile(
        SessionQueueReadProfile profile, string word, string id)
    {
        SessionQueueReadProfiles.CapabilityId(profile).Should().Be(id);
        SessionQueueReadProfiles.Parse(word.ToUpperInvariant()).Should().Be(profile);
        var prefix = $"queue enqueue {Guid.NewGuid():D} 1 0 {Guid.NewGuid():D} {Guid.NewGuid():D} ";
        foreach (var activation in new[] { "", "Luna, " })
        {
            SessionCommand.Parse(activation + prefix + word, "Luna")!.QueueProfile.Should().Be(profile);
            var command = SessionCommand.Parse(activation + prefix + word + $" after {Guid.NewGuid():D}", "Luna")!;
            command.Operation.Should().Be(SessionCommandOperation.QueueEnqueue);
            command.QueueProfile.Should().Be(profile);
        }
        foreach (var injection in new[] { " {}", " count 1", " offset 1", " page 2", " after invalid", " {\"id\":\"other\"}" })
        {
            SessionCommand.Parse(prefix + word + injection, "Luna")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        }
        SessionCommand.Parse(prefix + "capabilities.get", "Luna")!.Operation.Should().Be(SessionCommandOperation.Invalid);
    }

    [Fact]
    public void Unknown_profile_values_and_partial_or_legacy_profile_formats_fail_closed()
    {
        SessionQueueReadProfiles.Parse("unknown").Should().BeNull();
        var unknown = () => SessionQueueReadProfiles.CapabilityId((SessionQueueReadProfile)99);
        unknown.Should().Throw<InvalidDataException>();
        var accepts = () => SessionQueueReadProfiles.Accepts((SessionQueueReadProfile)99, Reply(SessionQueueReadProfile.Version));
        accepts.Should().Throw<InvalidDataException>();
        var pending = Pending();
        foreach (var state in new[] { SessionQueueState.Pending, SessionQueueState.Cancelled, SessionQueueState.Removed,
            SessionQueueState.Expired, SessionQueueState.Interrupted })
        {
            (pending with { State = state }).ValidateStoredResult();
        }
        foreach (var invalid in new[]
        {
            pending with { Profile = null }, pending with { Profile = (SessionQueueReadProfile)99 },
            pending with { RecordVersion = null, PendingLifetimeMinutes = null },
            pending with { RecordVersion = 2 }, pending with { RecordVersion = 3 }, pending with { RecordVersion = 5 },
            pending with { State = SessionQueueState.Running }, pending with { State = SessionQueueState.Succeeded },
            pending with { State = SessionQueueState.Failed }, pending with { State = SessionQueueState.Unknown },
            pending with { Result = Reply(SessionQueueReadProfile.Capabilities) },
        })
        {
            invalid.Invoking(entry => entry.Validate()).Should().Throw<InvalidDataException>();
        }
        var legacy = pending with { RecordVersion = 2, Profile = null };
        (legacy with { Result = Reply(SessionQueueReadProfile.Version) }).Invoking(entry => entry.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Version)]
    [InlineData(SessionQueueReadProfile.Capabilities)]
    [InlineData(SessionQueueReadProfile.Readiness)]
    [InlineData(SessionQueueReadProfile.Runtimes)]
    public void Matching_bounded_first_page_is_the_only_success_and_capture_preserves_pending_clock(SessionQueueReadProfile profile)
    {
        var pending = Pending() with { Profile = profile };
        pending.ValidateStoredResult();
        var active = pending.Admit(new(2), 2, Now.AddSeconds(1), 5);
        active.RecordVersion.Should().Be(4);
        active.Profile.Should().Be(profile);
        active.ExpiresAt.Should().Be(pending.ExpiresAt);
        active.ActiveDeadlineAt.Should().Be(Now.AddSeconds(1).AddMinutes(5));
        active.ValidateStoredResult();
        var result = Reply(profile);
        var completed = active with { Revision = new(3), State = SessionQueueState.Succeeded, Result = result };
        completed.ValidateStoredResult();
        (completed with { Result = null }).Invoking(entry => entry.ValidateStoredResult()).Should().Throw<InvalidDataException>();
        foreach (var other in new[] { SessionQueueReadProfile.Version, SessionQueueReadProfile.Capabilities, SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes })
        {
            SessionQueueReadProfiles.Accepts(profile, Reply(other)).Should().Be(profile == other);
        }
        foreach (var malformed in new[]
        {
            result with { Outcome = CapabilityOutcome.Denied }, result with { Outcome = CapabilityOutcome.Failed },
            result with { Reason = new string('x', 4096) },
            result with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            result with { Runtime = new("unknown", RuntimeLocality.Local, CapabilityAvailability.Available, null, "x", null, false) },
        })
        {
            SessionQueueReadProfiles.Accepts(profile, malformed).Should().BeFalse();
            (completed with { Result = malformed }).Invoking(entry => entry.Validate()).Should().Throw<InvalidDataException>();
        }
        (active with { Result = result }).Invoking(entry => entry.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public void Serialized_bound_includes_framing_at_the_exact_byte_threshold(int bytes, bool accepted)
    {
        var baseline = Reply(SessionQueueReadProfile.Version);
        var reply = baseline with { Reason = new string('x', bytes - CapabilityReplyWire.GetSerializedSize(baseline) + baseline.Reason.Length) };
        CapabilityReplyWire.Serialize(reply).Length.Should().Be(bytes);
        SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.Version, reply).Should().Be(accepted);
    }

    [Fact]
    public void Page_shape_rejects_null_records_wrong_count_total_continuation_and_seventh_record()
    {
        foreach (var reply in new[]
        {
            new CapabilityReply(CapabilityOutcome.Succeeded, "x", Capabilities: new([null!], 1, null)),
            new CapabilityReply(CapabilityOutcome.Succeeded, "x", Readiness: new([null!], 1, null)),
            new CapabilityReply(CapabilityOutcome.Succeeded, "x", Runtimes: new([null!], 1, null)),
            new CapabilityReply(CapabilityOutcome.Succeeded, "x", Readiness: new(null!, 0, null)),
            new CapabilityReply(CapabilityOutcome.Succeeded, "x", Runtimes: new(null!, 0, null)),
        })
        {
            foreach (var profile in new[] { SessionQueueReadProfile.Version, SessionQueueReadProfile.Capabilities, SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes })
            {
                SessionQueueReadProfiles.Accepts(profile, reply).Should().BeFalse();
            }
        }
        foreach (var count in new[] { -1, 0, 5, 6, 7 })
        {
            foreach (var total in new[] { -1, 0, 5, 6, 7 })
            {
                foreach (var next in new int?[] { null, 5, 6, 7 })
                {
                    var records = count < 0 ? null! : Enumerable.Repeat(ReadOnlyCapabilityCatalog.Descriptors[0], count).ToArray();
                    var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Capabilities: new(records, total, next));
                    var valid = count >= 0 && count <= 6 && total >= count && count == Math.Min(total, 6) && next == (total > count ? count : null);
                    SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.Capabilities, reply).Should().Be(valid);
                }
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData(3)]
    public void Legacy_serialization_has_no_new_fields_and_round_trips_without_reclassification(int? version)
    {
        var entry = Pending() with { RecordVersion = version, Profile = null, PendingLifetimeMinutes = version is null ? null : 30 };
        if (version == 3) { entry = entry with { RecordVersion = 2 }; entry = entry.Admit(new(2), 2, Now, 5); }
        entry.ValidateStoredResult();
        var original = JsonSerializer.Serialize(entry);
        original.Should().NotContain("Profile").And.NotContain("Result").And.NotContain("ReadProfile");
        var reopened = JsonSerializer.Deserialize<SessionQueueEntry>(original)!;
        reopened.ValidateStoredResult();
        reopened.ReadProfile.Should().Be(SessionQueueReadProfile.Version);
        JsonSerializer.Serialize(reopened).Should().Be(original);
    }

    internal static CapabilityReply Reply(SessionQueueReadProfile profile) => profile switch
    {
        SessionQueueReadProfile.Version => new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0.0", "fixed")),
        SessionQueueReadProfile.Capabilities => new(CapabilityOutcome.Succeeded, "catalogue",
            Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors, 6, null)),
        SessionQueueReadProfile.Readiness => new(CapabilityOutcome.Succeeded, "recorded-observations",
            Readiness: new([new("kora.storage", null, CapabilityAvailability.Unavailable, "not-observed", null)], 1, null)),
        _ => new(CapabilityOutcome.Succeeded, "recorded-observation",
            Runtimes: new([new("local.inference", RuntimeLocality.Local, CapabilityAvailability.Unavailable, null, "not-observed", null, false)], 1, null)),
    };

    private static SessionQueueEntry Pending() => new(HostRequest.Create(RequestOrigin.LocalUi), new(1), new(1), 1,
        SessionQueueState.Pending, Guid.NewGuid(), 1, Now, Now.AddMinutes(30),
        RecordVersion: 4, PendingLifetimeMinutes: 30, Profile: SessionQueueReadProfile.Capabilities);
}
