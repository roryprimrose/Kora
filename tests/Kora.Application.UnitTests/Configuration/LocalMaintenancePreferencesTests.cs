using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Maintenance;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalMaintenancePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "maintenance-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 6)]
    [InlineData(true, false, 37)]
    [InlineData(true, true, 168)]
    public void ConfirmedAtomicSettingsSurviveRestartWithoutChangingOtherPreferences(bool enabled, bool beta, int hours)
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        var preferences = new LocalMaintenancePreferences(paths);
        preferences.Load().Should().BeNull();
        store.WriteText("unrelated.txt", "retained");
        var settings = new MaintenanceSettings(enabled, beta, hours);
        preferences.BeginWrite();
        preferences.Save(settings);
        var restarted = new LocalMaintenancePreferences(store);
        restarted.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        restarted.Load().Should().Be(settings);
        store.ReadText("unrelated.txt").Should().Be("retained");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1,1,0,0")]
    [InlineData("v1,1,0,169")]
    [InlineData("v1,1,0,6\n")]
    [InlineData("v1,1,0,6,extra")]
    public void CorruptSavedValuesRemainUnavailableAndUnchanged(string text)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("maintenance-settings.txt", text);
        var preferences = new LocalMaintenancePreferences(store);
        preferences.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        store.ReadText("maintenance-settings.txt").Should().Be(text);
    }

    [Fact]
    public void OversizedAndUnconfirmedFilesFailClosedEvenWithoutASettingsFile()
    {
        var store = new LocalPreferenceStore(new Paths(root));
        var preferences = new LocalMaintenancePreferences(store);
        preferences.BeginWrite();
        preferences.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        preferences.Load().Should().BeNull();
        store.WriteText("maintenance-settings.txt", new string('x', MaintenanceSettings.MaximumBytes + 1));
        preferences.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        store.WriteText("maintenance-settings.txt", MaintenanceSettings.Default.Serialize());
        store.WriteText("maintenance-settings-unconfirmed.txt", new string('x', MaintenanceSettings.MaximumBytes + 1));
        preferences.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        preferences.Load().Should().Be(MaintenanceSettings.Default);
    }

    [Fact]
    public void NullSettingsAreRejectedBeforeWriting()
    {
        var preferences = new LocalMaintenancePreferences(new Paths(root));
        preferences.Invoking(value => value.Save(null!)).Should().Throw<ArgumentNullException>();
        preferences.Load().Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}
