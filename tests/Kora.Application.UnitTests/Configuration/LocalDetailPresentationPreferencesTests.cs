using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalDetailPresentationPreferencesTests
{
    [Theory]
    [InlineData(DetailPresentationMode.Offer)]
    [InlineData(DetailPresentationMode.LinkOnly)]
    public void AtomicProtocolKeepsUnconfirmedValuesHeldAcrossReconstruction(DetailPresentationMode mode)
    {
        var store = new Store();
        var preferences = new LocalDetailPresentationPreferences(store);
        preferences.Load().Should().BeNull();
        store.Values.Should().BeEmpty();
        preferences.BeginWrite();
        preferences.Save(mode);
        preferences.ReadBack().Should().Be(mode);
        new LocalDetailPresentationPreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        new LocalDetailPresentationPreferences(store).Load().Should().Be(mode);
        store.Values.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, string>("detail-presentation.txt", DetailPresentationPreference.Format(mode)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Offer\n")]
    [InlineData("Linkonly")]
    [InlineData("OpenAutomatically")]
    public void CorruptSavedValuesRemainExplicitlyUnavailable(string text)
    {
        var store = new Store();
        store.Values["detail-presentation.txt"] = text;
        var read = () => new LocalDetailPresentationPreferences(store).Load();
        read.Should().Throw<InvalidDataException>();
        store.Values["detail-presentation.txt"].Should().Be(text);
    }

    [Fact]
    public void PublicConstructorOnlyResolvesApplicationDataPaths()
    {
        var root = Path.GetFullPath("unused-detail-preference-test");
        new LocalDetailPresentationPreferences(new Paths(root)).Should().NotBeNull();
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths { public string RoamingRoot => LocalRoot; }
    private sealed class Store : IPreferenceStore
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public string? ReadText(string fileName) => Values.GetValueOrDefault(fileName);
        public void WriteText(string fileName, string contents) => Values[fileName] = contents;
        public void Delete(string fileName) => Values.Remove(fileName);
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
    }
}
