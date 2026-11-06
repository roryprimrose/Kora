using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalStartupPresentationPreferencesTests
{
    [Theory]
    [InlineData(StartupPresentation.Tray)]
    [InlineData(StartupPresentation.Workspace)]
    [InlineData(null)]
    public void ConfirmedIntentAndResetSurviveReconstructionWithoutOtherWrites(StartupPresentation? value)
    {
        var store = new Store();
        store.Values["voice-consent.txt"] = "Off";
        store.Values["logon-startup.txt"] = "On";
        var p = new LocalStartupPresentationPreferences(store);
        p.Load().Should().BeNull();
        p.BeginWrite();
        p.Save(value);
        p.ReadBack().Should().Be(value);
        new LocalStartupPresentationPreferences(store).Invoking(x => x.Load()).Should().Throw<InvalidDataException>();
        p.ConfirmWrite();
        new LocalStartupPresentationPreferences(store).Load().Should().Be(value);
        store.Values["voice-consent.txt"].Should().Be("Off");
        store.Values["logon-startup.txt"].Should().Be("On");
        p.Invoking(x => x.Save((StartupPresentation)99)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Workspace")]
    [InlineData("v2:Workspace")]
    [InlineData("v1:Tray\n")]
    [InlineData("v1:workspace")]
    public void CorruptionIsNeverDefaultIntent(string value)
    {
        var store = new Store();
        store.Values["manual-startup-presentation.txt"] = value;
        new LocalStartupPresentationPreferences(store).Invoking(x => x.Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void PublicConstructionIsInert()
    {
        var path = Path.GetFullPath("unused-startup-presentation-test");
        new LocalStartupPresentationPreferences(new Paths(path)).Should().NotBeNull();
        Directory.Exists(path).Should().BeFalse();
    }

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
