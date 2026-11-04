using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalForegroundPttPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "ptt-" + Guid.NewGuid().ToString("N"));
    private LocalForegroundPttPreferences Create() => new(new Paths(root, root));
    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;

    [Fact]
    public void ExplicitChoicesUseAtomicStorageAndAnUnconfirmedWriteCannotReload()
    {
        var preferences = Create();
        preferences.Load().Should().Be(ForegroundPttShortcut.Unassigned);
        foreach (var choice in ForegroundPttShortcut.Choices)
        {
            preferences.BeginWrite();
            preferences.Save(choice);
            preferences.ReadBack().Should().Be(choice);
            Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            Create().Load().Should().Be(choice);
        }
        Directory.GetFiles(Path.Combine(root, "Preferences")).Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Control Shift Space\n")]
    [InlineData("Control C")]
    [InlineData("Alt F4")]
    [InlineData("Windows L")]
    public void CorruptSavedChordsAreNotReinterpreted(string contents)
    {
        Directory.CreateDirectory(Path.Combine(root, "Preferences"));
        File.WriteAllText(Path.Combine(root, "Preferences", "foreground-ptt-shortcut.txt"), contents);
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
}
