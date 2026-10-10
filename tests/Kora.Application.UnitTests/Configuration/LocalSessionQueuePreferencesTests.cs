using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalSessionQueuePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "queue-preferences-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("2\n1\n2\n120", 1, 2, 120)]
    [InlineData("2\ndefault\ndefault\ndefault", null, null, null)]
    [InlineData("2\n10\n1\n1", 10, 1, 1)]
    public void KnownLifetimeSchemaPreservesBytesAndUsesExplicitDefaultActiveBudgetUntilEdit(
        string content, int? capacity, int? slots, int? lifetime)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("session-queue.txt", content);
        var before = File.ReadAllBytes(Path.Combine(root, "Preferences", "session-queue.txt"));
        var preferences = new LocalSessionQueuePreferences(store);
        preferences.Load().Should().Be(new SessionQueuePreferences(capacity, slots, lifetime));
        preferences.Load().Limits.ActiveBudgetMinutes.Should().Be(5);
        File.ReadAllBytes(Path.Combine(root, "Preferences", "session-queue.txt")).Should().Equal(before);
        var edited = preferences.Load().With(SessionQueueOption.ActiveBudgetMinutes, "60");
        preferences.BeginWrite();
        preferences.Save(edited);
        preferences.ReadBack().Should().Be(edited);
        preferences.ConfirmWrite();
        new LocalSessionQueuePreferences(store).Load().Should().Be(edited);
        store.ReadLines("session-queue.txt")!.First().Should().Be("3");
    }

    [Fact]
    public void Atomic_overrides_readback_marker_restart_and_per_option_reset_preserve_unrelated_files()
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        var preferences = new LocalSessionQueuePreferences(paths);
        preferences.Load().Should().Be(new SessionQueuePreferences());
        store.WriteText("session-retention.txt", "independent");
        foreach (var saved in new[] { new SessionQueuePreferences(1, 2), new SessionQueuePreferences(10),
            new SessionQueuePreferences(null, 1), new SessionQueuePreferences(1, 2, 1),
            new SessionQueuePreferences(pendingLifetimeMinutes: 30), new SessionQueuePreferences(pendingLifetimeMinutes: 120),
            new SessionQueuePreferences(1, 2, 120, 1), new SessionQueuePreferences(activeBudgetMinutes: 5),
            new SessionQueuePreferences(activeBudgetMinutes: 60),
            new SessionQueuePreferences() })
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
    [InlineData("1\n1\n2", 1, 2)]
    [InlineData("1\ndefault\ndefault", null, null)]
    [InlineData("1\n10\n1", 10, 1)]
    public void Known_schema_one_preserves_overrides_without_writing_until_explicit_confirmed_edit(string content, int? capacity, int? slots)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("session-queue.txt", content);
        var before = store.ReadText("session-queue.txt");
        var preferences = new LocalSessionQueuePreferences(store);
        preferences.Load().Should().Be(new SessionQueuePreferences(capacity, slots));
        preferences.Load().Limits.PendingLifetimeMinutes.Should().Be(30);
        store.ReadText("session-queue.txt").Should().Be(before);
        var edited = preferences.Load().With(SessionQueueOption.PendingLifetimeMinutes, "120");
        preferences.BeginWrite();
        preferences.Save(edited);
        preferences.ReadBack().Should().Be(edited);
        store.ReadLines("session-queue.txt").Should().Equal("3",
            capacity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "default",
            slots?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "default", "120", "default");
        preferences.ConfirmWrite();
        new LocalSessionQueuePreferences(store).Load().Should().Be(edited);
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
    [InlineData("2\n10\n1\n0")]
    [InlineData("2\n10\n1\n121")]
    [InlineData("2\n10\n1\n030")]
    [InlineData("2\n10\n1\n30.0")]
    [InlineData("2\n10\n1\nDefault")]
    [InlineData("2\n10\n1\n")]
    [InlineData("2\n10\n1\n30\nextra")]
    [InlineData("3\n10\n1\n30")]
    [InlineData("3\n10\n1\n30\n0")]
    [InlineData("3\n10\n1\n30\n61")]
    [InlineData("3\n10\n1\n30\n05")]
    [InlineData("3\n10\n1\n30\n5.0")]
    [InlineData("3\n10\n1\n30\nDefault")]
    [InlineData("3\n10\n1\n30\n")]
    [InlineData("3\n10\n1\n30\n5\nextra")]
    [InlineData("4\n10\n1\n30\n5")]
    public void Unknown_corrupt_and_noncanonical_saved_values_never_default(string content)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("session-queue.txt", content);
        new LocalSessionQueuePreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Oversize_saved_lifetime_and_durable_marker_fail_closed_without_replacing_bytes()
    {
        var store = new LocalPreferenceStore(new Paths(root));
        var content = "2\n10\n1\n" + new string('1', 131072);
        store.WriteText("session-queue.txt", content);
        var preferences = new LocalSessionQueuePreferences(store);
        preferences.Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        File.ReadAllText(Path.Combine(root, "Preferences", "session-queue.txt")).Should().Be(content);
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
