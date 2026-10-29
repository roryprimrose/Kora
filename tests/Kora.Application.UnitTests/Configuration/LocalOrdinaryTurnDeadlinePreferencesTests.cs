using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalOrdinaryTurnDeadlinePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts",
        "ordinary-deadline-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AtomicPreferenceRoundtripRestartMarkerAndResetAreIndependentOfCompatibilityQueue()
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        store.WriteText("session-queue-settings.txt", "compatibility untouched");
        var preferences = new LocalOrdinaryTurnDeadlinePreferences(paths);
        preferences.Load().Should().BeNull();
        preferences.BeginWrite();
        preferences.Save(new(60));
        preferences.ReadBack().Should().Be(new OrdinaryTurnDeadline(60));
        preferences.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        new LocalOrdinaryTurnDeadlinePreferences(paths).Load().Should().Be(new OrdinaryTurnDeadline(60));
        preferences.BeginWrite();
        preferences.Save(null);
        preferences.ReadBack().Should().BeNull();
        preferences.ConfirmWrite();
        preferences.Load().Should().BeNull();
        store.ReadText("session-queue-settings.txt").Should().Be("compatibility untouched");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
        preferences.Invoking(value => value.Save(default(OrdinaryTurnDeadline))).Should().Throw<ArgumentOutOfRangeException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
