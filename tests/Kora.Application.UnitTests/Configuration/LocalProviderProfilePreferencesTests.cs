using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalProviderProfilePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", $"profile-preferences-{Guid.NewGuid():N}");
    private string DirectoryPath => Path.Combine(root, "Preferences");
    private string ProfilePath => Path.Combine(DirectoryPath, "provider-profiles.json");
    private LocalProviderProfilePreferences Create() => new(new Paths(root, root));

    [Fact]
    public void MissingStorageIsUnsetDesiredUntilInstallationObservationAndCreatesNothing()
    {
        Create().Load().Should().BeNull();
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void AtomicReadbackFenceAndRestartPreserveExactTuplesAndUnrelatedSettings()
    {
        var value = new ProviderProfilePreferences(ProviderService.Claude,
            [new(ProviderService.Claude, new("account"), new(42), new("model"), new("effort"), new("window"))]);
        var preferences = Create();
        preferences.BeginWrite();
        preferences.Save(value);
        preferences.ReadBack()!.Serialize().Should().Be(value.Serialize());
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        var companion = Path.Combine(DirectoryPath, "provider-mode.txt");
        File.WriteAllText(companion, "1\nLocalOnly");
        preferences.ConfirmWrite();
        Create().Load()!.Serialize().Should().Be(value.Serialize());
        preferences.Save(value.Select(ProviderService.Codex));
        Create().Load()!.For(ProviderService.Claude).Should().Be(value.For(ProviderService.Claude));
        File.ReadAllText(companion).Should().Be("1\nLocalOnly");
        Directory.GetFiles(DirectoryPath, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidUtf8OrOversizedSavedStateRemainsUnchangedAndUnavailable(bool oversized)
    {
        Directory.CreateDirectory(DirectoryPath);
        var bytes = oversized ? new byte[ProviderProfilePreferences.MaximumUtf8Bytes + 1] : new byte[] { 0xff };
        File.WriteAllBytes(ProfilePath, bytes);
        Create().Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        File.ReadAllBytes(ProfilePath).Should().Equal(bytes);
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
