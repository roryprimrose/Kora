using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Conversation;

public sealed class RetainedLocalObservationsTests
{
    [Theory]
    [InlineData(ReadOnlyCapabilityCatalog.List)]
    [InlineData(ReadOnlyCapabilityCatalog.Get)]
    [InlineData(ReadOnlyCapabilityCatalog.Version)]
    [InlineData(ReadOnlyCapabilityCatalog.Readiness)]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeList)]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeStatus)]
    public void SixExactShapesRetainOriginalBytesAndHostIdentityWithoutProviderCorrelation(string id)
    {
        var (input, reply) = Result(id);
        var first = RetainedLocalObservation.Capture(id, input, reply, DateTimeOffset.UnixEpoch);
        var second = RetainedLocalObservation.Capture(id, input, reply, DateTimeOffset.UnixEpoch);
        first.Id.Should().NotBeEmpty().And.NotBe(second.Id);
        Encoding.UTF8.GetBytes(first.ResultJson).Should().Equal(CapabilityReplyWire.Serialize(reply));
        var collection = new RetainedLocalObservations(1, [first, second]);
        collection.Validate();
        var readback = JsonSerializer.Deserialize<RetainedLocalObservations>(JsonSerializer.Serialize(collection))!;
        readback.Should().Be(collection);
        readback.GetHashCode().Should().Be(collection.GetHashCode());
        (collection with { Version = 2 }).Should().NotBe(collection);
        (collection with { Items = [second] }).Should().NotBe(collection);
        collection.Equals(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("empty")]
    [InlineData("default")]
    [InlineData("count")]
    [InlineData("null")]
    [InlineData("duplicate")]
    [InlineData("item")]
    public void CollectionRejectsUnknownVersionMissingOversizedOrForgedItems(string fault)
    {
        var item = Observation();
        var items = fault switch
        {
            "empty" => [],
            "default" => default,
            "count" => Enumerable.Range(0, 7).Select(_ => item with { Id = Guid.NewGuid() }).ToImmutableArray(),
            "null" => [null!],
            "duplicate" => [item, item],
            "item" => [item with { Id = Guid.Empty }],
            _ => ImmutableArray.Create(item),
        };
        new RetainedLocalObservations(fault is "version" ? 2 : 1, items)
            .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("time")]
    [InlineData("input-null")]
    [InlineData("result-null")]
    [InlineData("unsupported")]
    [InlineData("input")]
    [InlineData("json")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("null")]
    [InlineData("outcome")]
    [InlineData("reason")]
    [InlineData("canonical")]
    [InlineData("shape")]
    [InlineData("credential")]
    [InlineData("control")]
    [InlineData("noncharacter")]
    [InlineData("bound")]
    public void MalformedRestrictedOrUnsupportedSnapshotFailsWhole(string fault)
    {
        var item = Observation();
        item = fault switch
        {
            "id" => item with { Id = Guid.Empty },
            "time" => item with { ObservedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) },
            "input-null" => item with { InputJson = null! },
            "result-null" => item with { ResultJson = null! },
            "unsupported" => item with { CapabilityId = "mcp.example" },
            "input" => item with { InputJson = "{\"extra\":true}" },
            "json" => item with { ResultJson = "{" },
            "duplicate" => item with { ResultJson = item.ResultJson.Replace("\"reason\":", "\"reason\":\"first\",\"reason\":", StringComparison.Ordinal) },
            "unknown" => item with { ResultJson = item.ResultJson.Insert(1, "\"extra\":0,") },
            "null" => item with { ResultJson = "null" },
            "outcome" => item with { ResultJson = item.ResultJson.Replace("Succeeded", "Denied", StringComparison.Ordinal) },
            "reason" => item with { ResultJson = item.ResultJson.Replace("observed", "", StringComparison.Ordinal) },
            "canonical" => item with { ResultJson = " " + item.ResultJson },
            "shape" => item with { ResultJson = "{\"outcome\":\"Succeeded\",\"reason\":\"observed\"}" },
            "credential" => item with { ResultJson = item.ResultJson.Replace("\"deployment\"", "\"password=secret-example\"", StringComparison.Ordinal) },
            "control" => item with { ResultJson = item.ResultJson.Replace("\"deployment\"", "\"\\u0001\"", StringComparison.Ordinal) },
            "noncharacter" => item with { ResultJson = item.ResultJson.Replace("\"deployment\"", "\"\\uFFFE\"", StringComparison.Ordinal) },
            _ => item with { ResultJson = new string('x', 4097) },
        };
        var validate = () => item.Validate();
        if (fault is "credential") { validate.Should().Throw<ConversationCredentialException>(); }
        else { validate.Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void CaptureReusesLiveExactShapeAdmissionButHistoricalReadsDoNotCompareChangedCatalogueBodies()
    {
        var descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] with { SchemaVersion = 2 };
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Descriptor: descriptor);
        var capture = () => RetainedLocalObservation.Capture(ReadOnlyCapabilityCatalog.Get,
            "{\"id\":\"capabilities.list\"}", reply, DateTimeOffset.UnixEpoch);
        capture.Should().Throw<InvalidDataException>("capture must match the actual current host result shape");
        var historical = new RetainedLocalObservation(Guid.NewGuid(), ReadOnlyCapabilityCatalog.Get,
            "{\"id\":\"capabilities.list\"}", DateTimeOffset.UnixEpoch, Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(reply)));
        historical.Validate();
        (historical with { InputJson = "{\"id\":\"capabilities.get\"}" }).Invoking(item => item.Validate())
            .Should().Throw<InvalidDataException>();
        var listReply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed",
            Capabilities: new([descriptor], 1, null));
        FluentActions.Invoking(() => RetainedLocalObservation.Capture(ReadOnlyCapabilityCatalog.List, "{}",
            listReply, DateTimeOffset.UnixEpoch)).Should().Throw<InvalidDataException>();
        (historical with { CapabilityId = ReadOnlyCapabilityCatalog.List, InputJson = "{}",
            ResultJson = Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(listReply)) }).Validate();
        (historical with { ResultJson = historical.ResultJson.Replace("\"schemaVersion\":2", "\"schemaVersion\":0", StringComparison.Ordinal) })
            .Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failed")]
    [InlineData("hosted")]
    [InlineData("overflow")]
    [InlineData("legacy")]
    public void MessageRetainsOptionalCollectionOnlyWithSuccessfulLocalAnswerInsideUnchangedFullCell(string fault)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request,
            fault is "hosted" ? ModelProviderSelection.CopilotCandidate : ModelProviderSelection.OllamaCandidate);
        var message = new ConversationMessage(request, new(1), 1, 3, DateTimeOffset.UnixEpoch,
            fault is "overflow" ? new string('\u0800', 4096) : "original", ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, fault is "overflow" ? new string('a', 3900) : "answer"),
            Observations = new(1, fault is "overflow"
                ? Enumerable.Range(0, 6).Select(_ => RetainedLocalObservation.Capture(ReadOnlyCapabilityCatalog.Version, "{}",
                    new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", new string('b', 900))), DateTimeOffset.UnixEpoch)).ToImmutableArray()
                : [Observation()]),
        };
        if (fault is "failed") { message = message with { Outcome = ModelTurnOutcome.Unknown, AssistantResponse = null }; }
        if (fault is "legacy") { message = message with { Observations = null }; }
        if (fault is "success" or "legacy")
        {
            message.Validate();
            JsonSerializer.Deserialize<ConversationMessage>(JsonSerializer.Serialize(message)).Should().Be(message);
        }
        else { message.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("source")]
    [InlineData("kind")]
    [InlineData("revision")]
    [InlineData("collection")]
    public void ObservationDetailsBindExactHostItemAndInertSource(string fault)
    {
        var item = Observation();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var message = new ConversationMessage(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "original",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "answer"), Observations = new(1, [item]),
        };
        if (fault is "collection") { message = message with { Observations = null }; }
        var admit = () => new AdmittedDetailContent(new(new(fault is "missing" ? Guid.NewGuid() : item.Id), fault is "revision" ? 2 : 3),
            fault is "kind" ? DetailContentKind.Markdown : DetailContentKind.PlainText,
            DetailContentOrigin.RetainedLocalObservation, DetailSensitivity.DisclosureConfirmationRequired,
            "Observed version", "Original host observation, not current truth",
            fault is "source" ? "substitute" : item.ResultJson, conversationMessage: message);
        if (fault is "valid")
        {
            var content = admit();
            content.Source.Should().Be(item.ResultJson);
            content.IsSameSnapshot(admit()).Should().BeTrue();
        }
        else { admit.Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("id")]
    [InlineData("schema")]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("effect")]
    [InlineData("availability")]
    [InlineData("lanes-null")]
    [InlineData("lanes-empty")]
    [InlineData("lane")]
    [InlineData("limits-null")]
    [InlineData("input-bound")]
    [InlineData("record-bound")]
    [InlineData("output-bound")]
    public void HistoricalShapeValidationRejectsInvalidDescriptorsWithoutConsultingLiveDescriptorBodies(string fault)
    {
        var descriptor = ReadOnlyCapabilityCatalog.Descriptors[0];
        descriptor = fault switch
        {
            "null" => null!,
            "id" => descriptor with { Id = "unsupported" },
            "schema" => descriptor with { SchemaVersion = 0 },
            "input" => descriptor with { Input = (CapabilityInputShape)999 },
            "output" => descriptor with { Output = (CapabilityOutputShape)999 },
            "effect" => descriptor with { Effect = (CapabilityEffect)999 },
            "availability" => descriptor with { Availability = (CapabilityAvailability)999 },
            "lanes-null" => descriptor with { CallerLanes = null! },
            "lanes-empty" => descriptor with { CallerLanes = [] },
            "lane" => descriptor with { CallerLanes = [(CapabilityLane)999] },
            "limits-null" => descriptor with { Limits = null! },
            "input-bound" => descriptor with { Limits = descriptor.Limits with { MaximumInputUtf8Bytes = 0 } },
            "record-bound" => descriptor with { Limits = descriptor.Limits with { MaximumRecords = 0 } },
            _ => descriptor with { Limits = descriptor.Limits with { MaximumOutputUtf8Bytes = 0 } },
        };
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Capabilities: new([descriptor], 1, null));
        var historical = new RetainedLocalObservation(Guid.NewGuid(), ReadOnlyCapabilityCatalog.List, "{}",
            DateTimeOffset.UnixEpoch, Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(reply)));
        historical.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void LegacyMessageCannotAcquireAnObservationAndUnknownProtocolCannotBypassSupportedTools()
    {
        var legacy = new ConversationMessage(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 2,
            DateTimeOffset.UnixEpoch, "original", ModelTurnOutcome.Unavailable, ModelTurnReason.ProviderOutcome)
        { Observations = new(1, [Observation()]) };
        legacy.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ModelToolProtocol.ValidateRetainedReadOnlyResult("mcp.forged", "{}", "{}"))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CompleteSerializedCellAcceptsExactly32768BytesAndRejectsOneAdditionalByte()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var observation = Observation();
        ConversationMessage Message(int characters) =>
            new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, new string('\u0800', 4096),
                ModelTurnOutcome.Succeeded, ModelTurnReason.None)
            {
                Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
                AssistantResponse = new(provenance, new string('a', 3900)),
                Observations = new(1, [observation with
                {
                    ResultJson = Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(new(CapabilityOutcome.Succeeded,
                        "observed", Version: new("1.0", new string('b', characters))))),
                }]),
            };
        var lower = 0;
        var upper = 3900;
        while (lower < upper)
        {
            var middle = (lower + upper + 1) / 2;
            if (JsonSerializer.SerializeToUtf8Bytes(Message(middle)).Length <= ConversationMessage.MaximumStorageUtf8Bytes)
            { lower = middle; }
            else { upper = middle - 1; }
        }
        var accepted = Message(lower);
        JsonSerializer.SerializeToUtf8Bytes(accepted).Length.Should().Be(32768);
        accepted.Validate();
        var refused = Message(lower + 1);
        JsonSerializer.SerializeToUtf8Bytes(refused).Length.Should().Be(32769);
        refused.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    private static RetainedLocalObservation Observation() =>
        RetainedLocalObservation.Capture(ReadOnlyCapabilityCatalog.Version, "{}",
            new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "deployment")), DateTimeOffset.UnixEpoch);

    private static (string Input, CapabilityReply Reply) Result(string id)
    {
        var runtime = new RuntimeObservation(ReadOnlyCapabilityCatalog.LocalRuntimeId, RuntimeLocality.Local,
            CapabilityAvailability.NotObserved, null, "not observed", null, false);
        return id switch
        {
            ReadOnlyCapabilityCatalog.List => ("{}", new(CapabilityOutcome.Succeeded, "observed",
                Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors, 6, null))),
            ReadOnlyCapabilityCatalog.Get => ("{\"id\":\"capabilities.list\"}", new(CapabilityOutcome.Succeeded, "observed",
                Descriptor: ReadOnlyCapabilityCatalog.Descriptors[0])),
            ReadOnlyCapabilityCatalog.Readiness => ("{}", new(CapabilityOutcome.Succeeded, "observed",
                Readiness: new([new("local", null, CapabilityAvailability.NotObserved, "not observed", null)], 1, null))),
            ReadOnlyCapabilityCatalog.RuntimeList => ("{}", new(CapabilityOutcome.Succeeded, "observed", Runtimes: new([runtime], 1, null))),
            ReadOnlyCapabilityCatalog.RuntimeStatus => ("{\"id\":\"local.inference\"}", new(CapabilityOutcome.Succeeded, "observed", Runtime: runtime)),
            _ => ("{}", new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "deployment"))),
        };
    }
}
