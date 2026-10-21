using System.Text;
using System.Text.Json;

using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Hosting;

public sealed class SessionQueueTargetedReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    public static TheoryData<string> Targets => new(ReadOnlyCapabilityCatalog.Descriptors.Select(item => item.Id));

    [Fact]
    public void LocalEventsKeepClosedProfileTargetMetadataWithoutResultPayloadOrRetargeting()
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        var task = new HostId<TaskIdentity>(Guid.NewGuid());
        var item = new LocalEvent(LocalEvent.Identity(LocalEventSource.LocalVersionQueue, session.Value, task.Value), 1,
            LocalEventSource.LocalVersionQueue, LocalEventType.Queued, session, task, task.Value, 1, 1, 0,
            RequestOrigin.LocalUi, SessionQueueEligibility.Ready, Now, Now.AddMinutes(30));
        item.Validate();
        item.Summary.Should().Contain("local-version");
        var target = new SessionQueueCapabilityTarget(ReadOnlyCapabilityCatalog.Version);
        var capability = item with { QueueProfile = SessionQueueReadProfile.Capability, QueueTarget = target };
        capability.Validate();
        capability.Summary.Should().Contain("capabilities.get").And.Contain(target.Id).And.NotContain("local-version");
        var runtime = item with { QueueProfile = SessionQueueReadProfile.RuntimeStatus };
        runtime.Validate();
        runtime.Summary.Should().Contain("runtime.get_status").And.Contain("local.inference");
        capability.SameSource(capability with { ObservedAt = Now.AddSeconds(1) }).Should().BeTrue();
        item.SameSource(capability).Should().BeFalse();
        capability.SameSource(capability with { QueueTarget = new(ReadOnlyCapabilityCatalog.List) }).Should().BeFalse();
        foreach (var invalid in new[]
        {
            item with { QueueTarget = target }, capability with { QueueTarget = null },
            runtime with { QueueTarget = target }, capability with { QueueProfile = (SessionQueueReadProfile)99 },
            capability with { Source = LocalEventSource.LocalVersionQuestion },
        })
        {
            invalid.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        }
        var baseline = JsonSerializer.Serialize(item);
        baseline.Should().NotContain("QueueProfile").And.NotContain("QueueTarget");
        var state = new LocalEventBrokerState(1, Now, [new(capability, LocalEventDisposition.Presented, null, Now)], []);
        var encoded = LocalEventBrokerState.Serialize(state);
        var reopened = LocalEventBrokerState.Deserialize(encoded);
        reopened.Receipts.Single().Event.QueueTarget.Should().Be(target);
        reopened.Receipts.Single().Event.SameSource(capability).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void ExactCurrentTargetsRetainInternalIntegrityButCannotBeNewlyQueuedThroughTypedInput(string id)
    {
        var target = new SessionQueueCapabilityTarget(id);
        target.Descriptor.Should().BeSameAs(ReadOnlyCapabilityCatalog.FindExact(id));
        SessionQueueReadProfiles.CapabilityId(SessionQueueReadProfile.Capability).Should().Be(ReadOnlyCapabilityCatalog.Get);
        SessionQueueReadProfiles.Parse(ReadOnlyCapabilityCatalog.Get).Should().Be(SessionQueueReadProfile.Capability);
        SessionQueueReadProfiles.Describe(SessionQueueReadProfile.Capability, target).Should().Contain(id);
        using var input = JsonDocument.Parse(SessionQueueReadProfiles.Input(SessionQueueReadProfile.Capability, target));
        input.RootElement.EnumerateObject().Should().ContainSingle().Which.Name.Should().Be("id");
        input.RootElement.GetProperty("id").GetString().Should().Be(id);
        Encoding.UTF8.GetByteCount(input.RootElement.GetRawText()).Should().BeLessThanOrEqualTo(1024);
        var prefix = $"queue enqueue {Guid.NewGuid():D} 1 0 {Guid.NewGuid():D} {Guid.NewGuid():D} capabilities.get {id}";
        foreach (var activation in new[] { "", "Luna, " })
        {
            foreach (var suffix in new[] { "", $" after {Guid.NewGuid():D}" })
            {
                var command = SessionCommand.Parse(activation + prefix + suffix, "Luna")!;
                command.Operation.Should().Be(SessionCommandOperation.Invalid);
            }
        }
        var pending = Pending(SessionQueueReadProfile.Capability, target);
        pending.ValidateStoredResult();
        var active = pending.Admit(new(2), 2, Now, 5);
        active.Target.Should().Be(target);
        active.RecordVersion.Should().Be(5);
        active.ExpiresAt.Should().Be(pending.ExpiresAt);
        var success = active with { Revision = new(3), State = SessionQueueState.Succeeded, Result = DescriptorReply(target) };
        success.ValidateStoredResult();
        var bytes = JsonSerializer.Serialize(success);
        var decoded = JsonSerializer.Deserialize<SessionQueueEntry>(bytes)!;
        decoded.ValidateStoredResult();
        JsonSerializer.Serialize(decoded).Should().Be(bytes);
        bytes.Should().Contain("\"Target\":{\"Id\":").And.NotContain("\"Target\":{\"Descriptor\"");
        foreach (var other in ReadOnlyCapabilityCatalog.Descriptors)
        {
            SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.Capability,
                DescriptorReply(new(other.Id)), target).Should().Be(string.Equals(id, other.Id, StringComparison.Ordinal));
        }
        (success with { Result = null }).Invoking(entry => entry.ValidateStoredResult()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("APPLICATION.GET_VERSION")]
    [InlineData("version")]
    [InlineData("local.inference")]
    [InlineData("network.get_web_page")]
    [InlineData("{\"id\":\"application.get_version\"}")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownAliasedCaseChangedOrRawTargetsFailAtDomainAndGrammar(string? id)
    {
        var target = new SessionQueueCapabilityTarget(id!);
        var validate = () => SessionQueueReadProfiles.Validate(SessionQueueReadProfile.Capability, target);
        validate.Should().Throw<InvalidDataException>();
        Pending(SessionQueueReadProfile.Capability, target).Invoking(entry => entry.Validate()).Should().Throw<InvalidDataException>();
        var command = SessionCommand.Parse($"queue enqueue {Guid.NewGuid():D} 1 0 {Guid.NewGuid():D} {Guid.NewGuid():D} capabilities.get {id}", "Luna")!;
        command.Operation.Should().Be(SessionCommandOperation.Invalid);
    }

    [Fact]
    public void FixedRuntimeHasNoUserTargetAndOriginalFourInputsRemainExactlyEmpty()
    {
        SessionQueueReadProfiles.Parse(ReadOnlyCapabilityCatalog.RuntimeStatus).Should().Be(SessionQueueReadProfile.RuntimeStatus);
        SessionQueueReadProfiles.CapabilityId(SessionQueueReadProfile.RuntimeStatus).Should().Be(ReadOnlyCapabilityCatalog.RuntimeStatus);
        SessionQueueReadProfiles.Input(SessionQueueReadProfile.RuntimeStatus).Should().Be("{\"id\":\"local.inference\"}");
        SessionQueueReadProfiles.Describe(SessionQueueReadProfile.RuntimeStatus).Should().Contain(ReadOnlyCapabilityCatalog.LocalRuntimeId);
        foreach (var profile in new[] { SessionQueueReadProfile.Version, SessionQueueReadProfile.Capabilities, SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes })
        {
            SessionQueueReadProfiles.Input(profile).Should().Be("{}");
            SessionQueueReadProfiles.Describe(profile).Should().Be(SessionQueueReadProfiles.CapabilityId(profile));
        }
        var prefix = $"queue enqueue {Guid.NewGuid():D} 1 0 {Guid.NewGuid():D} {Guid.NewGuid():D} ";
        foreach (var activation in new[] { "", "Luna, " })
        {
            SessionCommand.Parse(activation + prefix + "runtime.get_status", "Luna")!.Operation.Should().Be(SessionCommandOperation.Invalid);
            SessionCommand.Parse(activation + prefix + $"runtime.get_status after {Guid.NewGuid():D}", "Luna")!.Operation.Should().Be(SessionCommandOperation.Invalid);
            foreach (var injection in new[] { " local.inference", " other", " {}", " id local.inference", " count 1", " offset 1", " after invalid", " after", " after x extra" })
            {
                SessionCommand.Parse(activation + prefix + "runtime.get_status" + injection, "Luna")!.Operation.Should().Be(SessionCommandOperation.Invalid);
                SessionCommand.Parse(activation + prefix + "capabilities.get application.get_version" + injection, "Luna")!.Operation.Should().Be(SessionCommandOperation.Invalid);
            }
        }
    }

    [Fact]
    public void ExplicitVersionRejectsPartialFutureMismatchedAndNonTargetProfilesWithoutRewritingLegacy()
    {
        var target = new SessionQueueCapabilityTarget(ReadOnlyCapabilityCatalog.Version);
        var capability = Pending(SessionQueueReadProfile.Capability, target);
        var runtime = Pending(SessionQueueReadProfile.RuntimeStatus);
        foreach (var invalid in new[]
        {
            capability with { Target = null }, capability with { Profile = null },
            capability with { RecordVersion = 4 }, capability with { RecordVersion = 6 },
            capability with { RecordVersion = null }, capability with { RecordVersion = 2 }, capability with { RecordVersion = 3 },
            runtime with { Target = target }, runtime with { RecordVersion = 4 },
            runtime with { Profile = SessionQueueReadProfile.Version },
            runtime with { Profile = SessionQueueReadProfile.Capabilities },
            runtime with { PendingLifetimeMinutes = null }, runtime with { State = SessionQueueState.Running },
            runtime with { Result = RuntimeReply() },
        })
        {
            invalid.Invoking(entry => entry.Validate()).Should().Throw<InvalidDataException>();
        }
        foreach (var profile in Enum.GetValues<SessionQueueReadProfile>().Where(profile => profile != SessionQueueReadProfile.Capability))
        {
            var validate = () => SessionQueueReadProfiles.Validate(profile, target);
            validate.Should().Throw<InvalidDataException>();
        }
        var missing = () => SessionQueueReadProfiles.Input(SessionQueueReadProfile.Capability);
        missing.Should().Throw<InvalidDataException>();
        foreach (var version in new int?[] { null, 2, 3, 4 })
        {
            var original = capability with { Target = null, Profile = version == 4 ? SessionQueueReadProfile.Capabilities : null,
                RecordVersion = version, PendingLifetimeMinutes = version is null ? null : 30 };
            if (version == 3) { original = (original with { RecordVersion = 2 }).Admit(new(2), 2, Now, 5); }
            original.ValidateStoredResult();
            var canonical = JsonSerializer.Serialize(original);
            canonical.Should().NotContain("\"Target\"");
            var reopened = JsonSerializer.Deserialize<SessionQueueEntry>(canonical)!;
            reopened.ValidateStoredResult();
            JsonSerializer.Serialize(reopened).Should().Be(canonical);
            (original with { Target = target }).Invoking(entry => entry.Validate()).Should().Throw<InvalidDataException>();
        }
        runtime.Admit(new(2), 2, Now, 5).RecordVersion.Should().Be(5);
    }

    [Fact]
    public void OnlyOneExactTypedDescriptorWithCurrentCatalogueShapeIsAccepted()
    {
        var target = new SessionQueueCapabilityTarget(ReadOnlyCapabilityCatalog.Version);
        var baseline = DescriptorReply(target);
        var descriptor = target.Descriptor;
        var malformed = new[]
        {
            descriptor with { Id = "unknown" }, descriptor with { SchemaVersion = 2 },
            descriptor with { Input = CapabilityInputShape.Id }, descriptor with { Output = CapabilityOutputShape.Runtime },
            descriptor with { Effect = (CapabilityEffect)99 }, descriptor with { Availability = CapabilityAvailability.NotObserved },
            descriptor with { Limits = new(1024, 6, 4097) }, descriptor with { CallerLanes = null! },
            descriptor with { CallerLanes = [] },
        };
        foreach (var item in malformed)
        {
            SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.Capability, baseline with { Descriptor = item }, target).Should().BeFalse();
        }
        foreach (var reply in ExtraFields(baseline).Concat(new[]
        {
            baseline with { Descriptor = null }, baseline with { Outcome = CapabilityOutcome.Denied },
            baseline with { Outcome = CapabilityOutcome.Failed }, baseline with { Runtime = RuntimeReply().Runtime },
        }))
        {
            SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.Capability, reply, target).Should().BeFalse();
        }
        var active = Pending(SessionQueueReadProfile.Capability, target).Admit(new(2), 2, Now, 5);
        (active with { Result = baseline }).Invoking(entry => entry.Validate()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RecordedRuntimeTruthRequiresFixedLocalityUnqualifiedAndConsistentObservation()
    {
        var baseline = RuntimeReply();
        SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.RuntimeStatus, baseline).Should().BeTrue();
        foreach (var readiness in Enum.GetValues<DependencyReadiness>())
        {
            var observed = baseline.Runtime! with { Readiness = readiness, ObservedAt = Now,
                Availability = readiness == DependencyReadiness.Ready ? CapabilityAvailability.Available : CapabilityAvailability.Unavailable };
            SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.RuntimeStatus, baseline with { Runtime = observed }).Should().BeTrue();
            CapabilityReplyWire.IsRecordedLocalRuntime(observed with { Availability = CapabilityAvailability.NotObserved }).Should().BeFalse();
            CapabilityReplyWire.IsRecordedLocalRuntime(observed with { ObservedAt = null }).Should().BeFalse();
            CapabilityReplyWire.IsRecordedLocalRuntime(observed with { ObservedAt = Now.ToOffset(TimeSpan.FromHours(1)) }).Should().BeFalse();
        }
        foreach (var runtime in new[]
        {
            baseline.Runtime! with { Id = "other" }, baseline.Runtime! with { Locality = (RuntimeLocality)99 },
            baseline.Runtime! with { ToolLoopQualified = true }, baseline.Runtime! with { Reason = "" },
            baseline.Runtime! with { Reason = " " }, baseline.Runtime! with { Reason = null! },
            baseline.Runtime! with { Availability = CapabilityAvailability.Available },
            baseline.Runtime! with { ObservedAt = Now },
            baseline.Runtime! with { Readiness = (DependencyReadiness)99, ObservedAt = Now },
        })
        {
            SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.RuntimeStatus, baseline with { Runtime = runtime }).Should().BeFalse();
        }
        foreach (var reply in ExtraFields(baseline).Concat(new[]
        {
            baseline with { Runtime = null }, baseline with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            baseline with { Outcome = CapabilityOutcome.Denied }, baseline with { Outcome = CapabilityOutcome.Failed },
        }))
        {
            SessionQueueReadProfiles.Accepts(SessionQueueReadProfile.RuntimeStatus, reply).Should().BeFalse();
        }
        var active = Pending(SessionQueueReadProfile.RuntimeStatus).Admit(new(2), 2, Now, 5);
        (active with { State = SessionQueueState.Succeeded, Revision = new(3), Result = baseline }).ValidateStoredResult();
        (active with { State = SessionQueueState.Succeeded }).Invoking(entry => entry.ValidateStoredResult()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Capability, 4096, true)]
    [InlineData(SessionQueueReadProfile.Capability, 4097, false)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus, 4096, true)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus, 4097, false)]
    public void CompleteUtf8ResultFrameHonorsExactUnchangedThreshold(SessionQueueReadProfile profile, int bytes, bool accepted)
    {
        var target = profile == SessionQueueReadProfile.Capability ? new SessionQueueCapabilityTarget(ReadOnlyCapabilityCatalog.Version) : null;
        var baseline = target is null ? RuntimeReply() : DescriptorReply(target);
        var reply = baseline with { Reason = new string('x', bytes - CapabilityReplyWire.GetSerializedSize(baseline) + baseline.Reason.Length) };
        CapabilityReplyWire.Serialize(reply).Length.Should().Be(bytes);
        SessionQueueReadProfiles.Accepts(profile, reply, target).Should().Be(accepted);
    }

    private static IEnumerable<CapabilityReply> ExtraFields(CapabilityReply reply) =>
    [
        reply with { Version = new("1.0.0", "fixed") },
        reply with { Capabilities = new([], 0, null) },
        reply with { Readiness = new([], 0, null) },
        reply with { Runtimes = new([], 0, null) },
    ];
    private static CapabilityReply DescriptorReply(SessionQueueCapabilityTarget target) =>
        new(CapabilityOutcome.Succeeded, "admitted-read-only", Descriptor: target.Descriptor);
    private static CapabilityReply RuntimeReply() => new(CapabilityOutcome.Succeeded, "recorded-observation",
        Runtime: new(ReadOnlyCapabilityCatalog.LocalRuntimeId, RuntimeLocality.Local, CapabilityAvailability.NotObserved, null,
            "No completed observation is recorded.", null, false));
    private static SessionQueueEntry Pending(SessionQueueReadProfile profile, SessionQueueCapabilityTarget? target = null) =>
        new(HostRequest.Create(RequestOrigin.LocalUi), new(1), new(1), 1, SessionQueueState.Pending, Guid.NewGuid(), 1,
            Now, Now.AddMinutes(30), RecordVersion: 5, PendingLifetimeMinutes: 30, Profile: profile, Target: target);
}
