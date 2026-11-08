using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Configuration.SettingInspection;

public sealed class WindowsSpeechRateObservationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };
    private static DateTimeOffset Now => DateTimeOffset.UnixEpoch;
    private static WindowsSpeechRateObservation Rate(int desired = 0, int? effective = null, bool saved = false,
        string provider = SpeechProviderIds.Kokoro, SpeechRateSupport support = SpeechRateSupport.Unsupported,
        long providerRevision = 7, long revision = 9) =>
        new(desired, effective, saved, provider, support, providerRevision, revision);
    private static ImmutableArray<SettingObservation> Values(WindowsSpeechRateObservation rate) =>
        [new(new SpeechSelection(rate.ProviderId, null), false, rate.ProviderRevision), new(rate)];

    [Theory]
    [InlineData(-10, -10, true, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative)]
    [InlineData(10, 10, true, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative)]
    [InlineData(0, 0, false, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative)]
    [InlineData(0, null, false, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative)]
    [InlineData(5, null, true, SpeechProviderIds.Kokoro, SpeechRateSupport.Unsupported)]
    [InlineData(0, null, false, SpeechProviderIds.Windows, SpeechRateSupport.Unsupported)]
    public void ClosedNativeDesiredAndConfiguredMappingRoundTripWithoutScalarFallback(int desired, int? effective,
        bool saved, string provider, SpeechRateSupport support)
    {
        var rate = Rate(desired, effective, saved, provider, support);
        var observation = new SettingObservation(rate);
        observation.Value.Should().BeNull();
        observation.QuietHours.Should().BeNull();
        observation.Selection.Should().BeNull();
        observation.IsSaved.Should().Be(saved);
        observation.Revision.Should().Be(9);
        observation.Text.Should().Contain("desired " + desired).And.Contain("provider revision 7")
            .And.Contain("rate revision 9").And.Contain("Kokoro unchanged").And.Contain("not actual playback speed")
            .And.Contain(saved ? "confirmed saved" : "confirmed absent");
        foreach (var options in new[] { new JsonSerializerOptions(), Web })
        {
            var json = JsonSerializer.Serialize(observation, options);
            JsonSerializer.Deserialize<SettingObservation>(json, options).Should().Be(observation);
            var typed = JsonSerializer.Serialize(rate, options);
            JsonSerializer.Deserialize<WindowsSpeechRateObservation>(typed, options).Should().Be(rate);
            using var document = JsonDocument.Parse(typed);
            var effectiveField = options.PropertyNamingPolicy?.ConvertName("Effective") ?? "Effective";
            document.RootElement.TryGetProperty(effectiveField, out var mapped).Should().BeTrue();
            mapped.ValueKind.Should().Be(effective is null ? JsonValueKind.Null : JsonValueKind.Number);
        }
    }

    [Theory]
    [InlineData(-11, null, true, SpeechProviderIds.Windows, SpeechRateSupport.Unsupported, 1, 1)]
    [InlineData(11, null, true, SpeechProviderIds.Windows, SpeechRateSupport.Unsupported, 1, 1)]
    [InlineData(0, 11, false, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative, 1, 1)]
    [InlineData(0, 0, false, SpeechProviderIds.Kokoro, SpeechRateSupport.Unsupported, 1, 1)]
    [InlineData(0, null, false, SpeechProviderIds.Kokoro, SpeechRateSupport.WindowsNative, 1, 1)]
    [InlineData(0, null, false, "unknown", SpeechRateSupport.Unsupported, 1, 1)]
    [InlineData(0, null, false, SpeechProviderIds.Windows, (SpeechRateSupport)99, 1, 1)]
    [InlineData(0, 1, false, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative, 1, 1)]
    [InlineData(1, null, false, SpeechProviderIds.Windows, SpeechRateSupport.Unsupported, 1, 1)]
    [InlineData(0, null, false, SpeechProviderIds.Windows, SpeechRateSupport.Unsupported, 0, 1)]
    [InlineData(0, null, false, SpeechProviderIds.Windows, SpeechRateSupport.Unsupported, 1, 0)]
    public void InvalidRateProviderProvenanceAndRevisionCombinationsAreRejected(int desired, int? effective, bool saved,
        string provider, SpeechRateSupport support, long providerRevision, long revision) =>
        FluentActions.Invoking(() => Rate(desired, effective, saved, provider, support, providerRevision, revision))
            .Should().Throw<Exception>();

    [Theory]
    [InlineData("missing-effective")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("scalar")]
    [InlineData("mixed-selection")]
    [InlineData("mixed-quiet")]
    [InlineData("null-rate")]
    [InlineData("saved")]
    [InlineData("revision")]
    [InlineData("text")]
    [InlineData("provider-null")]
    [InlineData("fractional")]
    [InlineData("root-array")]
    public void DedicatedWireRejectsMissingNullUnknownMixedAndForgedFields(string defect)
    {
        var node = JsonSerializer.SerializeToNode(new SettingObservation(Rate()), Web)!.AsObject();
        var rate = node["windowsRate"]!.AsObject();
        switch (defect)
        {
            case "missing-effective": rate.Remove("effective"); break;
            case "extra": rate["recovery"] = "private exception path"; break;
            case "scalar": node["value"] = 0; break;
            case "mixed-selection": node["selection"] = null; break;
            case "mixed-quiet": node["quietHours"] = "Off"; break;
            case "null-rate": node["windowsRate"] = null; break;
            case "saved": node["isSaved"] = true; break;
            case "revision": node["revision"] = 8; break;
            case "text": node["text"] = "effective zero"; break;
            case "provider-null": rate["providerId"] = null; break;
            case "fractional": rate["desired"] = 0.5m; break;
            case "root-array": node["windowsRate"] = new JsonArray(); break;
        }
        var json = node.ToJsonString();
        if (string.Equals(defect, "duplicate", StringComparison.Ordinal)) { json = json.Replace("\"desired\":0", "\"desired\":0,\"desired\":0", StringComparison.Ordinal); }
        FluentActions.Invoking(() => JsonSerializer.Deserialize<SettingObservation>(json, Web)).Should().Throw<Exception>();
    }

    [Theory]
    [InlineData(ModelToolMode.None, RequestOrigin.LocalUi)]
    [InlineData(ModelToolMode.SettingsDiscovery, RequestOrigin.ActivatedVoice)]
    public void DedicatedReviewBindsExactOriginalProfileSourceAndOnlyPermittedDiscovery(ModelToolMode mode, RequestOrigin origin)
    {
        var values = Values(Rate(4, null, true));
        var original = HostRequest.Create(origin);
        var review = new SettingInspectionReview(values, "Which saved Windows rate is effective and why is Kokoro unchanged?",
            original, Now, mode, SettingInspectionProfile.WindowsRate);
        review.Profile.Should().Be(SettingInspectionProfile.WindowsRate);
        review.Observations.Should().Equal(values);
        review.Text.Should().Contain("NATIVE SOURCE PROFILE: WindowsRate").And.Contain("NEW Local context conversation")
            .And.Contain(review.Question).And.Contain("known unsupported/unavailable").And.Contain("not zero");
        var context = review.Bind(review.Destination, new(1), Now);
        context.UserRequest.Should().Be(review.Question);
        review.Destination.Origin.Should().Be(origin);
        context.Evidence.Single().Content.Should().Be(review.Source);
        review.TryConsume().Should().BeTrue();
        review.TryConsume().Should().BeFalse();
        if (mode == ModelToolMode.SettingsDiscovery)
        {
            var metadata = review.Source[review.Source.IndexOf('{', StringComparison.Ordinal)..];
            using var document = JsonDocument.Parse(metadata);
            document.RootElement.GetProperty("rows").GetArrayLength().Should().Be(2);
            var list = SettingsDiscoveryTools.Read(values, SettingsDiscoveryTools.List, "{}", review.Profile);
            list.Settings!.Profile.Should().Be(review.Profile);
            list.Settings.Records.Select(item => item.Descriptor.Setting).Should().Equal(SettingInspectionReview.WindowsRateSettings);
            ModelToolProtocol.CreateToolResult(new("list_1", SettingsDiscoveryTools.List, "{}"), list);
            var get = SettingsDiscoveryTools.Read(values, SettingsDiscoveryTools.Get, """{"id":"speech.windows-rate"}""", review.Profile);
            ModelToolProtocol.CreateToolResult(new("get_1", SettingsDiscoveryTools.Get, """{"id":"speech.windows-rate"}"""), get);
            get.Settings!.Observation.Should().BeSameAs(values[1]);
            SettingsDiscoveryTools.Read(values, SettingsDiscoveryTools.Get, """{"id":"appearance.response-timeout"}""", review.Profile)
                .Outcome.Should().Be(CapabilityOutcome.Unavailable);
            SettingsDiscoveryTools.Read(values, SettingsDiscoveryTools.List, """{"offset":2}""", review.Profile)
                .Outcome.Should().Be(CapabilityOutcome.Denied);
            var forged = get with { Settings = get.Settings with { Profile = null } };
            FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(new("get_1", SettingsDiscoveryTools.Get,
                """{"id":"speech.windows-rate"}"""), forged)).Should().Throw<InvalidDataException>();
        }
        else { context.Tools.Should().BeEmpty(); }
        SettingsDiscoveryTools.HasConfirmedReader(RegisteredSetting.WindowsSpeechRate).Should().BeFalse();
        SettingsDiscoveryTools.Read(values, SettingsDiscoveryTools.Get, """{"id":"speech.windows-rate"}""")
            .Outcome.Should().Be(CapabilityOutcome.Unavailable);
        FluentActions.Invoking(() => new SettingObservation(RegisteredSetting.WindowsSpeechRate, 0, false, 1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("identity")]
    [InlineData("revision")]
    [InlineData("unknown-profile")]
    [InlineData("default-profile")]
    public void DedicatedProfileNeverAcceptsPartialMixedOrMismatchedSource(string defect)
    {
        var values = Values(Rate());
        values = defect switch
        {
            "missing" => values.RemoveAt(1),
            "extra" => values.Add(new(RegisteredSetting.Theme, 0, false, 1)),
            "duplicate" => values.Add(values[1]),
            "identity" => values.SetItem(0, new(SpeechSelection.Default, false, 7)),
            "revision" => values.SetItem(0, new(new SpeechSelection(SpeechProviderIds.Kokoro, null), false, 8)),
            _ => values,
        };
        var profile = string.Equals(defect, "unknown-profile", StringComparison.Ordinal) ? (SettingInspectionProfile)99
            : string.Equals(defect, "default-profile", StringComparison.Ordinal) ? SettingInspectionProfile.ConfirmedSettings : SettingInspectionProfile.WindowsRate;
        FluentActions.Invoking(() => new SettingInspectionReview(values, "Why?", HostRequest.Create(RequestOrigin.LocalUi), Now,
            ModelToolMode.None, profile)).Should().Throw<InvalidDataException>();
        if (profile != SettingInspectionProfile.ConfirmedSettings)
        { FluentActions.Invoking(() => SettingsDiscoveryTools.Read(values, SettingsDiscoveryTools.List, "{}", profile))
            .Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData(SettingInspectionProfile.ConfirmedSettings)]
    [InlineData((SettingInspectionProfile)99)]
    public void ExplicitDefaultAndUnknownResultProfileTagsCannotWidenDedicatedResults(SettingInspectionProfile forged)
    {
        const string input = """{"id":"speech.windows-rate"}""";
        var reply = SettingsDiscoveryTools.Read(Values(Rate()), SettingsDiscoveryTools.Get, input, SettingInspectionProfile.WindowsRate);
        reply = reply with { Settings = reply.Settings! with { Profile = forged } };
        SettingsDiscoveryTools.Matches(reply, SettingsDiscoveryTools.Get,
            CapabilityInputPolicy.Read(SettingsDiscoveryTools.FindExact(SettingsDiscoveryTools.Get)!, input)).Should().BeFalse();
        FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(new("get_1", SettingsDiscoveryTools.Get, input), reply))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"ConfirmedSettings\"")]
    [InlineData("1")]
    [InlineData("\"windowsrate\"")]
    public void DefaultWireCannotAcquireAnExplicitProfileOrWindowsRateField(string profile)
    {
        var list = SettingsDiscoveryTools.Read([], SettingsDiscoveryTools.List, "{}");
        var json = JsonSerializer.SerializeToNode(list.Settings, Web)!.AsObject();
        json["profile"] = JsonNode.Parse(profile);
        FluentActions.Invoking(() => JsonSerializer.Deserialize<RegisteredSettingsResult>(json.ToJsonString(), Web))
            .Should().Throw<JsonException>();
        var scalar = JsonSerializer.SerializeToNode(new SettingObservation(RegisteredSetting.Theme, 0, false, 1), Web)!.AsObject();
        scalar["windowsRate"] = null;
        FluentActions.Invoking(() => JsonSerializer.Deserialize<SettingObservation>(scalar.ToJsonString(), Web))
            .Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteDefaultAndDedicatedResultWireRoundTripWithExplicitProfileOnlyForDedicatedSource(bool dedicated)
    {
        var profile = dedicated ? SettingInspectionProfile.WindowsRate : SettingInspectionProfile.ConfirmedSettings;
        var result = SettingsDiscoveryTools.Read(dedicated ? Values(Rate()) : [], SettingsDiscoveryTools.List, "{}", profile).Settings!;
        foreach (var options in new[] { new JsonSerializerOptions(), Web })
        {
            var json = JsonSerializer.Serialize(result, options);
            var parsed = JsonSerializer.Deserialize<RegisteredSettingsResult>(json, options)!;
            parsed.Records.Should().Equal(result.Records);
            parsed.TotalRecords.Should().Be(result.TotalRecords);
            parsed.NextOffset.Should().Be(result.NextOffset);
            parsed.Profile.Should().Be(result.Profile);
            parsed.Observation.Should().BeNull();
            if (dedicated)
            {
                var get = SettingsDiscoveryTools.Read(Values(Rate()), SettingsDiscoveryTools.Get,
                    """{"id":"speech.windows-rate"}""", profile).Settings!;
                JsonSerializer.Deserialize<RegisteredSettingsResult>(JsonSerializer.Serialize(get, options), options)!
                    .Observation.Should().Be(get.Observation);
            }
        }
    }

    [Fact]
    public void DirectRegisteredResultDecoderRejectsNullRatherThanCreatingAnEmptySuccess()
    {
        static RegisteredSettingsResult ReadNull()
        {
            var reader = new Utf8JsonReader("null"u8);
            reader.Read();
            return new RegisteredSettingsResult.Converter().Read(ref reader, typeof(RegisteredSettingsResult), Web);
        }
        FluentActions.Invoking(ReadNull).Should().Throw<JsonException>();
    }
}
