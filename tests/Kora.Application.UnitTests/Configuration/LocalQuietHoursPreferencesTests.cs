using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalQuietHoursPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".scratch", "orchestration",
        "scheduled-quiet-hours", "preferences-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AtomicSaveReloadOffAndResetPreserveUnrelatedPreferencesAndPendingEvidence()
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        var preferences = new LocalQuietHoursPreferences(paths);
        preferences.Load().Should().BeNull();
        store.WriteText("unrelated.txt", "retained");
        var schedule = QuietHoursSchedule.Weekly("Mon,Fri", "22:00", "06:00", "UTC");
        preferences.BeginWrite();
        preferences.Save(schedule);
        preferences.ReadBack().Should().Be(schedule);
        var restarted = new LocalQuietHoursPreferences(store);
        FluentActions.Invoking(() => restarted.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        restarted.Load().Should().Be(schedule);
        preferences.Save(QuietHoursSchedule.Off);
        restarted.Load().Should().Be(QuietHoursSchedule.Off);
        preferences.BeginWrite();
        preferences.Save(null);
        preferences.ReadBack().Should().BeNull();
        FluentActions.Invoking(() => restarted.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        restarted.Load().Should().BeNull();
        store.ReadText("unrelated.txt").Should().Be("retained");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("2\nOff")]
    [InlineData("1\r\nOff")]
    [InlineData("1\nOff\n")]
    [InlineData("1\nOff\nextra")]
    [InlineData("1\nweekly Mon 09:00 09:00 UTC")]
    [InlineData("1\nweekly Mon 09:00 10:00 not-an-installed-zone")]
    [InlineData("1\nweekly Mon 09:00 10:00 utc")]
    public void UnknownSchemaMalformedScheduleOrZoneNeverBecomesOff(string bytes)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("notices-quiet-hours.txt", bytes);
        FluentActions.Invoking(() => new LocalQuietHoursPreferences(store).Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void OversizeAndInvalidUtf8StayUnavailable()
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("notices-quiet-hours.txt", new string('x', 513));
        FluentActions.Invoking(() => new LocalQuietHoursPreferences(store).Load()).Should().Throw<InvalidDataException>();
        File.WriteAllBytes(Path.Combine(root, "Preferences", "notices-quiet-hours.txt"), [0xff]);
        FluentActions.Invoking(() => new LocalQuietHoursPreferences(store).Load()).Should().Throw<InvalidDataException>();
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
