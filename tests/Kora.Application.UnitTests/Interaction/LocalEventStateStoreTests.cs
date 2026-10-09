using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed class LocalEventStateStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "local-events-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Authoritative_atomic_store_roundtrips_only_bounded_metadata_and_pending_writes_hold_across_restart()
    {
        var paths = new Paths(root);
        var preferences = new LocalPreferenceStore(paths);
        var store = new LocalEventStateStore(preferences);
        store.Load().Should().BeNull();
        preferences.WriteText("unrelated.txt", "preserved");
        var state = LocalEventBrokerState.Empty(DateTimeOffset.UtcNow);
        store.BeginWrite();
        store.Save(state);
        store.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        store.ConfirmWrite();
        new LocalEventStateStore(paths).Load().Should().BeEquivalentTo(state);
        preferences.ReadText("unrelated.txt").Should().Be("preserved");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
        preferences.WriteText("local-events.json", "{\"Schema\":2}");
        store.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.WriteText("local-events.json", new string('x', LocalEventBrokerState.MaximumBytes + 1));
        store.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("local-events.json")]
    [InlineData("local-events-unconfirmed.txt")]
    public void Malformed_UTF8_state_or_marker_is_never_defaulted(string file)
    {
        var paths = new Paths(root);
        var preferences = new LocalPreferenceStore(paths);
        preferences.WriteText(file, "1");
        File.WriteAllBytes(Path.Combine(root, "Preferences", file), [0xC3, 0x28]);
        new LocalEventStateStore(paths).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Missing_saved_state_or_commit_marker_is_not_reinterpreted_as_first_run_and_confirmation_requires_exact_readback()
    {
        var preferences = new LocalPreferenceStore(new Paths(root));
        var store = new LocalEventStateStore(preferences);
        var state = LocalEventBrokerState.Empty(DateTimeOffset.UtcNow);
        store.BeginWrite();
        store.Save(state);
        store.ConfirmWrite();
        preferences.Delete("local-events.json");
        new LocalEventStateStore(preferences).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.WriteText("local-events.json", LocalEventBrokerState.Serialize(state));
        preferences.Delete("local-events-unconfirmed.txt");
        new LocalEventStateStore(preferences).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        new LocalEventStateStore(new FailedReadback()).Invoking(value => value.ConfirmWrite()).Should().Throw<InvalidDataException>();
        new LocalEventStateStore(new FailedReadback { FailMarker = true }).Invoking(value => value.BeginWrite()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Failed_exact_readback_remains_unconfirmed_not_rolled_back()
    {
        var preferences = new FailedReadback();
        var store = new LocalEventStateStore(preferences);
        store.BeginWrite();
        store.Invoking(value => value.Save(LocalEventBrokerState.Empty(DateTimeOffset.UtcNow))).Should().Throw<InvalidDataException>();
        store.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }
    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
    private sealed class FailedReadback : IPreferenceStore
    {
        internal bool FailMarker { get; init; }
        public string? ReadText(string fileName) =>
            string.Equals(fileName, "local-events-unconfirmed.txt", StringComparison.Ordinal) && !FailMarker ? "1" : "corrupt";
        public string[]? ReadLines(string fileName) => throw new InvalidOperationException("Unused.");
        public void WriteText(string fileName, string contents) { }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new InvalidOperationException("Unused.");
        public void Delete(string fileName) => throw new InvalidOperationException("Unused.");
    }
}
