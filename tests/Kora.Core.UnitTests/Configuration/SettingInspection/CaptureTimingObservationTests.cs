using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Tools;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Configuration.SettingInspection;

public sealed class CaptureTimingObservationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData(RegisteredSetting.CommandStartWait, "7.1234567")]
    [InlineData(RegisteredSetting.ReplyStartWait, "20.7654321")]
    [InlineData(RegisteredSetting.TrailingSilence, "0.5000001")]
    public void ValidatedTimingKeepsExactDecimalSecondsThroughReviewedToolWire(RegisteredSetting setting, string seconds)
    {
        var timing = new VoiceCaptureTiming(7.1234567m, 20.7654321m, 0.5000001m);
        var observation = new SettingObservation(setting, timing, true, long.MaxValue);
        observation.Selection.Should().BeNull();
        observation.Text.Should().Be($"{RegisteredSettingRegistry.Get(setting).Id} = {seconds} seconds (confirmed saved; matches live service)");
        var input = JsonSerializer.Serialize(new { id = RegisteredSettingRegistry.Get(setting).Id });
        var reply = SettingsDiscoveryTools.Read([observation], SettingsDiscoveryTools.Get, input);
        reply.Outcome.Should().Be(CapabilityOutcome.Succeeded);
        using var wire = JsonDocument.Parse(CapabilityReplyWire.Serialize(reply));
        var value = wire.RootElement.GetProperty("settings").GetProperty("observation");
        value.GetProperty("value").GetDecimal().Should().Be(observation.Value);
        value.GetProperty("revision").GetInt64().Should().Be(long.MaxValue);
        value.GetProperty("isSaved").GetBoolean().Should().BeTrue();
        value.GetProperty("text").GetString().Should().Be(observation.Text);
        value.TryGetProperty("selection", out _).Should().BeFalse();
        value.TryGetProperty("quietHours", out _).Should().BeFalse();
        JsonSerializer.Deserialize<SettingObservation>(value.GetRawText(), Options).Should().Be(observation);
    }

    [Theory]
    [InlineData("ReaderTextScale", "125.5")]
    [InlineData("ReaderTextScale", "2147483648")]
    [InlineData("ReaderTextScale", "\"125\"")]
    [InlineData("CommandStartWait", "true")]
    public void ScalarWireRejectsFractionalIntegerOverflowAndNonNumericValues(string setting, string value)
    {
        var json = $$"""{"setting":"{{setting}}","value":{{value}},"isSaved":false,"revision":0,"text":"forged"}""";
        FluentActions.Invoking(() => JsonSerializer.Deserialize<SettingObservation>(json, Options))
            .Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("CommandStartWait", "1")]
    [InlineData("ReplyStartWait", "61")]
    [InlineData("TrailingSilence", "0.50000001")]
    [InlineData("TrailingSilence", "0.500000000000000000000000000001")]
    public void TimingWireCannotRoundOrBypassDomainBounds(string setting, string value)
    {
        var json = $$"""{"setting":"{{setting}}","value":{{value}},"isSaved":false,"revision":0,"text":"forged"}""";
        FluentActions.Invoking(() => JsonSerializer.Deserialize<SettingObservation>(json, Options))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void InvalidDomainForeignSettingAndNegativeRevisionCannotEnterObservation()
    {
        foreach (var timing in new VoiceCaptureTiming[] { new(1, 15, 1), new(5, 61, 1), new(5, 15, 0.4m), new(5, 15, 0.50000001m) })
        {
            FluentActions.Invoking(() => new SettingObservation(RegisteredSetting.TrailingSilence, timing, false, 1))
                .Should().Throw<ArgumentOutOfRangeException>();
        }
        FluentActions.Invoking(() => new SettingObservation(RegisteredSetting.Theme, VoiceCaptureTiming.Default, false, 1))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new SettingObservation(RegisteredSetting.CommandStartWait, VoiceCaptureTiming.Default, false, -1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
