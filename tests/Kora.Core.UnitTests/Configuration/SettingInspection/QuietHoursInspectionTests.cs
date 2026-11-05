using AwesomeAssertions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Configuration.SettingInspection;

public sealed class QuietHoursInspectionTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void ConfiguredQuietHoursHasAConfirmedReader() =>
        SettingsDiscoveryTools.HasConfirmedReader(RegisteredSetting.QuietHours).Should().BeTrue();

    [Theory]
    [InlineData("Off", false)]
    [InlineData("Off", true)]
    [InlineData("weekly Mon,Wed,Fri 22:00 07:00 UTC", true)]
    [InlineData("weekly Mon,Tue,Wed,Thu,Fri,Sat,Sun 00:01 23:59 UTC", false)]
    public void TypedSchedulePreservesExactIntentProvenanceAndBoundedWire(string schedule, bool saved)
    {
        var domain = QuietHoursSchedule.Parse(schedule);
        var observation = new SettingObservation(domain, saved, 42);
        observation.Setting.Should().Be(RegisteredSetting.QuietHours);
        observation.QuietHours.Should().Be(domain);
        FluentActions.Invoking(() => observation.Value).Should().Throw<InvalidOperationException>();
        observation.Text.Should().Contain(schedule).And.Contain("configured intent only")
            .And.Contain(saved ? "confirmed saved" : "confirmed absent");
        var reply = SettingsDiscoveryTools.Read([observation], SettingsDiscoveryTools.Get, """{"id":"notices.quiet-hours"}""");
        var wire = CapabilityReplyWire.Serialize(reply);
        wire.Length.Should().BeLessThanOrEqualTo(SettingsDiscoveryTools.FindExact(SettingsDiscoveryTools.Get)!.Limits.MaximumOutputUtf8Bytes);
        using var json = JsonDocument.Parse(wire);
        var value = json.RootElement.GetProperty("settings").GetProperty("observation");
        value.TryGetProperty("value", out _).Should().BeFalse();
        value.GetProperty("quietHours").GetString().Should().Be(schedule);
        JsonSerializer.Deserialize<SettingObservation>(value.GetRawText(), Options).Should().Be(observation);
        ModelToolProtocol.CreateToolResult(new("quiet_1", SettingsDiscoveryTools.Get, """{"id":"notices.quiet-hours"}"""), reply)
            .Result.Should().Equal(wire);
    }

    [Fact]
    public void ScalarApiAndWireRemainExactAndCannotAcceptCompoundCasts()
    {
        var scalar = new SettingObservation(RegisteredSetting.ReaderTextScale, 125, true, 7);
        scalar.Value.Should().Be(125);
        scalar.QuietHours.Should().BeNull();
        var wire = JsonSerializer.Serialize(scalar, Options);
        wire.Should().Be("""{"setting":"ReaderTextScale","value":125,"isSaved":true,"revision":7,"text":"appearance.reader-text-scale = 125 percent (confirmed saved; matches live service)"}""");
        JsonSerializer.Deserialize<SettingObservation>(wire, Options).Should().Be(scalar);
        FluentActions.Invoking(() => new SettingObservation(RegisteredSetting.QuietHours, 0, false, 0))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new SettingObservation(null!, false, 0)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new SettingObservation(QuietHoursSchedule.Off, false, -1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"setting":21,"value":0,"isSaved":false,"revision":0,"text":"forged"}""")]
    [InlineData("""{"setting":0,"quietHours":"Off","isSaved":false,"revision":0,"text":"forged"}""")]
    [InlineData("""{"setting":0,"value":0,"isSaved":false,"revision":0,"text":"forged"}""")]
    [InlineData("""{"setting":21,"quietHours":"Off","isSaved":false,"revision":0,"text":"forged"}""")]
    [InlineData("""{"setting":21,"quietHours":"weekly Mon 22:00 07:00 utc","isSaved":false,"revision":0,"text":"forged"}""")]
    [InlineData("""{"setting":21,"quietHours":"weekly Mon 22:00 07:00 Etc/UTC","value":0,"isSaved":false,"revision":0,"text":"forged"}""")]
    public void ForgedWireCannotCreateASettingObservation(string json)
    {
        var converter = new SettingObservationJsonConverter();
        FluentActions.Invoking(() => Read(converter, json, Options)).Should().Throw<JsonException>();
    }

    private static SettingObservation Read(SettingObservationJsonConverter converter, string json, JsonSerializerOptions options)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return converter.Read(ref reader, typeof(SettingObservation), options);
    }
}
