using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalSpeechTextPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "captions-" + Guid.NewGuid().ToString("N"));
    private LocalSpeechTextPreferences Create() => new(new Paths(root, root));
    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;

    [Fact]
    public void Absent_atomic_save_readback_pending_restart_and_reset_have_no_content_persistence()
    {
        var preferences = Create();
        preferences.Load().Should().BeNull();
        preferences.BeginWrite();
        preferences.Save(SpeechTextMode.CurrentUtterance);
        preferences.ReadBack().Should().Be(SpeechTextMode.CurrentUtterance);
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        Create().Load().Should().Be(SpeechTextMode.CurrentUtterance);
        preferences.BeginWrite();
        preferences.Save(SpeechTextMode.Off);
        preferences.ConfirmWrite();
        Create().Load().Should().Be(SpeechTextMode.Off);
        Directory.GetFiles(Path.Combine(root, "Preferences")).Should().ContainSingle()
            .Which.Should().EndWith("speech-text-mode.txt");
        File.ReadAllText(Path.Combine(root, "Preferences", "speech-text-mode.txt")).Should().Be("Off");
        preferences.Invoking(item => item.Save((SpeechTextMode)100)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("Off\n")]
    [InlineData("CurrentSentence")]
    public void Corruption_never_becomes_a_default(string contents)
    {
        Directory.CreateDirectory(Path.Combine(root, "Preferences"));
        File.WriteAllText(Path.Combine(root, "Preferences", "speech-text-mode.txt"), contents);
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
}
