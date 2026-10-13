using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalReaderTextScalePreferencesTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public void ConfirmedValuesRoundTripAndPendingEvidenceFailsClosed(int percent)
    {
        var store = new Store();
        var preferences = new LocalReaderTextScalePreferences(store);
        preferences.LoadReaderTextScalePercent().Should().BeNull();
        preferences.BeginReaderTextScaleWrite();
        preferences.SaveReaderTextScalePercent(percent);
        preferences.ReadBackReaderTextScalePercent().Should().Be(percent);
        var pending = preferences.LoadReaderTextScalePercent;
        pending.Should().Throw<InvalidDataException>();
        preferences.ConfirmReaderTextScaleWrite();
        new LocalReaderTextScalePreferences(store).LoadReaderTextScalePercent().Should().Be(percent);
        store.Values.Keys.Should().Equal("reader-text-scale-percent.txt");
    }

    [Theory]
    [InlineData("")]
    [InlineData("100\n")]
    [InlineData(" 100")]
    [InlineData("+100")]
    [InlineData("0100")]
    [InlineData("100.0")]
    [InlineData("100%")]
    [InlineData("unknown")]
    [InlineData("2147483648")]
    [InlineData("74")]
    [InlineData("201")]
    public void SavedMalformedOrUnknownValuesAreNeverDefaults(string text)
    {
        var store = new Store();
        store.Values["reader-text-scale-percent.txt"] = text;
        var load = new LocalReaderTextScalePreferences(store).LoadReaderTextScalePercent;
        load.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(74)]
    [InlineData(201)]
    public void InvalidWritesDoNotReachAtomicStore(int percent)
    {
        var store = new Store();
        var action = () => new LocalReaderTextScalePreferences(store).SaveReaderTextScalePercent(percent);
        action.Should().Throw<ArgumentOutOfRangeException>();
        store.Values.Should().BeEmpty();
    }

    [Theory]
    [InlineData("75", AppearanceCommandOperation.Set)]
    [InlineData("100", AppearanceCommandOperation.Set)]
    [InlineData("200", AppearanceCommandOperation.Set)]
    [InlineData("74", AppearanceCommandOperation.Clarify)]
    [InlineData("201", AppearanceCommandOperation.Clarify)]
    [InlineData("100.5", AppearanceCommandOperation.Clarify)]
    [InlineData("one hundred", AppearanceCommandOperation.Clarify)]
    [InlineData("100%", AppearanceCommandOperation.Clarify)]
    public void ExactParserUsesCanonicalRegistryAndIntegerPercent(string text, AppearanceCommandOperation operation)
    {
        var command = AppearanceCommand.Parse("set appearance.reader-text-scale to " + text, "Kora")!;
        command.Operation.Should().Be(operation);
        command.Descriptor!.Option.Should().Be(AppearanceOption.ReaderTextScale);
    }

    [Theory]
    [InlineData("get", AppearanceCommandOperation.Get)]
    [InlineData("status", AppearanceCommandOperation.Get)]
    [InlineData("reset", AppearanceCommandOperation.Reset)]
    public void ExactDiscoveryAndResetShareOneId(string verb, AppearanceCommandOperation operation) =>
        AppearanceCommand.Parse(verb + " appearance.reader-text-scale", "Kora")!.Operation.Should().Be(operation);

    private sealed class Store : IPreferenceStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public string? ReadText(string fileName) => Values.GetValueOrDefault(fileName);
        public void WriteText(string fileName, string contents) => Values[fileName] = contents;
        public void Delete(string fileName) => Values.Remove(fileName);
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
    }
}
