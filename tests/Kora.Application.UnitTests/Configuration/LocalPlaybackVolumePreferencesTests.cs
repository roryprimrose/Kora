using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalPlaybackVolumePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "volume-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_is_unsaved_unity_and_atomic_restart_reset_preserves_other_options()
    {
        var paths = new Paths(root);
        var preferences = new LocalPlaybackVolumePreferences(paths);
        preferences.Load().Should().BeNull();
        var store = new LocalPreferenceStore(paths);
        store.WriteText("speech-provider.txt", "retained");
        foreach (var value in new[] { 0, 1, 100 })
        {
            preferences.Save(new(value));
            new LocalPlaybackVolumePreferences(store).Load().Should().Be(new PlaybackVolume(value));
        }
        preferences.Reset();
        new LocalPlaybackVolumePreferences(paths).Load().Should().BeNull();
        store.ReadText("speech-provider.txt").Should().Be("retained");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2\n100")]
    [InlineData("1\n100\nextra")]
    [InlineData("1\n")]
    [InlineData("1\n-1")]
    [InlineData("1\n101")]
    [InlineData("1\n 1")]
    [InlineData("1\n01")]
    [InlineData("1\n1.0")]
    public void Invalid_saved_format_never_becomes_default(string content)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("speech-playback-volume.txt", content);
        var action = new LocalPlaybackVolumePreferences(store).Load;
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Unconfirmed_writes_remain_unavailable_after_restart_until_exact_evidence_is_confirmed()
    {
        var paths = new Paths(root);
        var preferences = new LocalPlaybackVolumePreferences(paths);
        preferences.Save(new(30));
        preferences.BeginWrite();
        preferences.Save(new(1));
        preferences.ReadBack().Should().Be(new PlaybackVolume(1));
        var restarted = new LocalPlaybackVolumePreferences(paths);
        restarted.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        restarted.Load().Should().Be(new PlaybackVolume(1));
        preferences.BeginWrite();
        preferences.Reset();
        restarted.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.ReadBack().Should().BeNull();
        preferences.ConfirmWrite();
        restarted.Load().Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
