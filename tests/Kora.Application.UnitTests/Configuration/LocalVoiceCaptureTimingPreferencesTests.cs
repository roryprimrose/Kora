using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalVoiceCaptureTimingPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "capture-timing-" + Guid.NewGuid().ToString("N"));
    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
    private LocalVoiceCaptureTimingPreferences Create() => new(new Paths(root, root));

    [Fact]
    public void AtomicTimingReloadKeepsCompanionPreferencesAndUnconfirmedFence()
    {
        var preferences = Create();
        preferences.Load().Should().BeNull();
        var store = new LocalPreferenceStore(new Paths(root, root));
        store.WriteText("unrelated.txt", "unchanged");
        var timing = new VoiceCaptureTiming(7, 20, 2);
        preferences.BeginWrite();
        preferences.Save(timing);
        new LocalVoiceCaptureTimingPreferences(store).ReadBack().Should().Be(timing);
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        Create().Load().Should().Be(timing);
        store.ReadText("unrelated.txt").Should().Be("unchanged");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("1\n5\n15\n4")]
    public void MalformedStorageCannotBecomeDefaultTiming(string contents)
    {
        var store = new LocalPreferenceStore(new Paths(root, root));
        store.WriteText("voice-capture-timing.txt", contents);
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
}
