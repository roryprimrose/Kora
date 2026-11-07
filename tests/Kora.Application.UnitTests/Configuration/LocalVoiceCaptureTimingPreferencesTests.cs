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
        var timing = new VoiceCaptureTiming(7, 20, 2, 40);
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
    [InlineData("2\n5\n15\n1\n9")]
    [InlineData("2\n5\n15\n1\n61")]
    [InlineData("2\n5\n15\n1\n40.00000001")]
    public void MalformedStorageCannotBecomeDefaultTiming(string contents)
    {
        var store = new LocalPreferenceStore(new Paths(root, root));
        store.WriteText("voice-capture-timing.txt", contents);
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void LegacyAtomicPreferenceRecoversWithoutRewritingAndNextSaveUsesVersionTwo()
    {
        var store = new LocalPreferenceStore(new Paths(root, root));
        store.WriteText("voice-capture-timing.txt", "1\n7\n20\n2");
        var preferences = Create();
        preferences.Load().Should().Be(new VoiceCaptureTiming(7, 20, 2, 60));
        store.ReadText("voice-capture-timing.txt").Should().Be("1\n7\n20\n2");
        preferences.Save(preferences.Load()!.With(VoiceCaptureTiming.MaximumCaptureId, "40"));
        store.ReadText("voice-capture-timing.txt").Should().Be("2\n7\n20\n2\n40");
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
}
