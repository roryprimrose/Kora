using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalSessionQueuePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "queue-preferences-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Atomic_overrides_readback_marker_restart_and_per_option_reset_preserve_unrelated_files()
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        var preferences = new LocalSessionQueuePreferences(paths);
        preferences.Load().Should().Be(new SessionQueuePreferences());
        store.WriteText("session-retention.txt", "independent");
        foreach (var saved in new[] { new SessionQueuePreferences(1, 2), new SessionQueuePreferences(10),
            new SessionQueuePreferences(null, 1), new SessionQueuePreferences() })
        {
            preferences.BeginWrite();
            preferences.Save(saved);
            preferences.ReadBack().Should().Be(saved);
            preferences.Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            new LocalSessionQueuePreferences(store).Load().Should().Be(saved);
        }
        store.ReadText("session-retention.txt").Should().Be("independent");
        store.ReadText("session-queue.txt").Should().BeNull();
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
        preferences.Invoking(item => item.Save(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2\n10\n1")]
    [InlineData("1\n10\n1\nextra")]
    [InlineData("1\n0\n1")]
    [InlineData("1\n11\n1")]
    [InlineData("1\n10\n3")]
    [InlineData("1\n01\n1")]
    [InlineData("1\n10\n01")]
    [InlineData("1\nDefault\n1")]
    [InlineData("1\n10\n")]
    public void Unknown_corrupt_and_noncanonical_saved_values_never_default(string content)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("session-queue.txt", content);
        new LocalSessionQueuePreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_UTF8_in_preferences_or_marker_is_not_a_default(bool marker)
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        store.WriteText("session-queue.txt", "1\n10\n1");
        File.WriteAllBytes(Path.Combine(root, "Preferences", marker ? "session-queue-unconfirmed.txt" : "session-queue.txt"), [0xc3, 0x28]);
        new LocalSessionQueuePreferences(paths).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
