using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalModelProviderModePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", $"provider-preferences-{Guid.NewGuid():N}");
    private string DirectoryPath => Path.Combine(root, "Preferences");
    private string ModePath => Path.Combine(DirectoryPath, "provider-mode.txt");
    private LocalModelProviderModePreferences Create() => new(new Paths(root, root));

    [Fact]
    public void MissingStorageIsUnsavedLocalOnlyWithoutCreatingFiles()
    {
        Create().Load().Should().BeNull();
        Directory.Exists(root).Should().BeFalse();
    }

    [Theory]
    [InlineData(ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.LocalFirst)]
    [InlineData(ModelProviderMode.HostedPreferred)]
    public void AtomicReplacementAndRestartReadBackExactModes(ModelProviderMode mode)
    {
        var preferences = Create();
        preferences.Save(ModelProviderMode.LocalOnly);
        preferences.BeginWrite();
        preferences.Save(mode);
        preferences.ReadBack().Should().Be(mode);
        Create().Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        Create().Load().Should().Be(mode);
        File.ReadAllLines(ModePath).Should().Equal("1", mode.ToString());
        Directory.GetFiles(DirectoryPath, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("LocalOnly")]
    [InlineData("2\nLocalOnly")]
    [InlineData("1\nUnknown")]
    [InlineData("1\n1")]
    [InlineData("1\nlocalonly")]
    [InlineData("1\n LocalOnly")]
    [InlineData("1\nLocalOnly\nextra")]
    [InlineData("1\nLocalOnly\n\n")]
    public void MalformedSavedStateNeverBecomesDefault(string text)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(ModePath, text);
        Create().Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        File.ReadAllText(ModePath).Should().Be(text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidUtf8AndOversizedStateAreRejected(bool oversized)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllBytes(ModePath, oversized ? new byte[65] : [0xff]);
        Create().Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void InvalidModeCannotReplaceExistingStateOrAffectOtherPreferences()
    {
        var preferences = Create();
        preferences.Save(ModelProviderMode.LocalFirst);
        var companion = Path.Combine(DirectoryPath, "unrelated.txt");
        File.WriteAllText(companion, "preserve");
        preferences.Invoking(value => value.Save(ModelProviderMode.Unknown)).Should().Throw<ArgumentOutOfRangeException>();
        preferences.Load().Should().Be(ModelProviderMode.LocalFirst);
        File.ReadAllText(companion).Should().Be("preserve");
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
