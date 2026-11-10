using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalWorkbenchPlacementPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts",
        "workbench-placement-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Restart_restores_both_modes_and_preserves_unrelated_preferences()
    {
        var paths = new Paths(root);
        var preferences = new LocalWorkbenchPlacementPreferences(paths);
        preferences.Load().Should().BeNull();
        var store = new LocalPreferenceStore(paths);
        store.WriteText("unrelated.txt", "unchanged");
        var placement = new WorkbenchPlacement(new(-120, 25, 480, 360, 1),
            new(50, 20, 1280, 820, 1.5), true);
        preferences.Save(placement);
        new LocalWorkbenchPlacementPreferences(store).Load().Should().Be(placement);
        store.ReadText("unrelated.txt").Should().Be("unchanged");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
        preferences.Invoking(value => value.Save(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("1\n-\n-\nMinimized")]
    public void Invalid_saved_state_is_not_reinterpreted(string text)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("workbench-placement.txt", text);
        new LocalWorkbenchPlacementPreferences(store).Invoking(value => value.Load())
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Oversized_state_and_obstructed_path_are_unavailable()
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("workbench-placement.txt", new string('x', 4097));
        var preferences = new LocalWorkbenchPlacementPreferences(store);
        preferences.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        store.Delete("workbench-placement.txt");
        Directory.CreateDirectory(Path.Combine(root, "Preferences", "workbench-placement.txt"));
        preferences.Invoking(value => value.Load()).Should().Throw<UnauthorizedAccessException>();
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
