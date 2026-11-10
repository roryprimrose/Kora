using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalMarkdownDisplayPreferencesTests
{
    [Theory]
    [InlineData(MarkdownDisplayMode.Rendered)]
    [InlineData(MarkdownDisplayMode.Source)]
    public void AtomicProtocolHoldsUnconfirmedWritesAcrossRestart(MarkdownDisplayMode mode)
    {
        var store = new Store();
        var preferences = new LocalMarkdownDisplayPreferences(store);
        preferences.Load().Should().BeNull();
        store.Values.Should().BeEmpty();
        preferences.BeginWrite();
        preferences.Save(mode);
        preferences.ReadBack().Should().Be(mode);
        new LocalMarkdownDisplayPreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        new LocalMarkdownDisplayPreferences(store).Load().Should().Be(mode);
        store.Values.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, string>("markdown-display.txt", MarkdownDisplayPreference.Format(mode)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Source\n")]
    [InlineData("source")]
    [InlineData("OpenAutomatically")]
    public void CorruptionIsNotReinterpreted(string text)
    {
        var store = new Store();
        store.Values["markdown-display.txt"] = text;
        new LocalMarkdownDisplayPreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        store.Values["markdown-display.txt"].Should().Be(text);
    }

    [Fact]
    public void PublicConstructionOnlyResolvesApplicationPaths() =>
        new LocalMarkdownDisplayPreferences(new Paths(Path.GetFullPath("unused-markdown-preference-test"))).Should().NotBeNull();

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths { public string RoamingRoot => LocalRoot; }
    internal sealed class Store : IPreferenceStore
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public string? ReadText(string fileName) => Values.GetValueOrDefault(fileName);
        public void WriteText(string fileName, string contents) => Values[fileName] = contents;
        public void Delete(string fileName) => Values.Remove(fileName);
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
    }
}
