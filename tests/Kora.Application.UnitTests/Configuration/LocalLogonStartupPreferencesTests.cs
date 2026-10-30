using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalLogonStartupPreferencesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void ExactValuesAndUnconfirmedWritesSurviveReconstructionWithoutReinterpretation(bool? enabled)
    {
        var store = new Store();
        var preference = new LocalLogonStartupPreferences(store);
        preference.Load().Should().BeNull();
        preference.BeginWrite();
        preference.Save(enabled);
        preference.ReadBack().Should().Be(enabled);
        new LocalLogonStartupPreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        preference.ConfirmWrite();
        new LocalLogonStartupPreferences(store).Load().Should().Be(enabled);
        store.Values.ContainsKey("logon-startup.txt").Should().Be(enabled is not null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("on")]
    [InlineData("Off\n")]
    public void MalformedPreferenceFailsClosed(string value)
    {
        var store = new Store();
        store.Values["logon-startup.txt"] = value;
        new LocalLogonStartupPreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void PublicConstructorResolvesPathsWithoutWriting()
    {
        var paths = new Paths(Path.GetFullPath("unused-logon-test-path"));
        new LocalLogonStartupPreferences(paths).Should().NotBeNull();
        Directory.Exists(paths.LocalRoot).Should().BeFalse();
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths { public string RoamingRoot => LocalRoot; }
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
