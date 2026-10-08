using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalAuditRetentionPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "audit-retention-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Exact_bounds_reset_and_unconfirmed_restart_preserve_ordinary_settings_and_atomic_cleanup()
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        var preferences = new LocalAuditRetentionPreferences(paths);
        preferences.Load().Should().BeNull();
        store.WriteText("sqlite-diagnostic-retention.txt", "1\n14");
        foreach (var days in new[] { 30, 90, 365 })
        {
            preferences.BeginWrite();
            preferences.Save(new(days));
            preferences.ReadBack().Should().Be(new AuditRetentionDays(days));
            new LocalAuditRetentionPreferences(paths).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            new LocalAuditRetentionPreferences(paths).Load().Should().Be(new AuditRetentionDays(days));
        }
        preferences.BeginWrite();
        preferences.Reset();
        preferences.ReadBack().Should().BeNull();
        preferences.Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        preferences.Load().Should().BeNull();
        store.ReadText("sqlite-diagnostic-retention.txt").Should().Be("1\n14");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
        preferences.Invoking(item => item.Save(default)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2\n90")]
    [InlineData("1\n90\nextra")]
    [InlineData("1\n")]
    [InlineData("1\n29")]
    [InlineData("1\n366")]
    [InlineData("1\n-1")]
    [InlineData("1\n 30")]
    [InlineData("1\n030")]
    [InlineData("1\n30.0")]
    public void Corrupt_or_unknown_saved_format_is_explicitly_invalid(string text)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("audit-retention.txt", text);
        new LocalAuditRetentionPreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Malformed_UTF8_of_saved_value_or_pending_marker_is_never_defaulted(bool marker)
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        store.WriteText("audit-retention.txt", "1\n90");
        File.WriteAllBytes(Path.Combine(root, "Preferences", marker ? "audit-retention-unconfirmed.txt" : "audit-retention.txt"), [0xC3, 0x28]);
        new LocalAuditRetentionPreferences(paths).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
