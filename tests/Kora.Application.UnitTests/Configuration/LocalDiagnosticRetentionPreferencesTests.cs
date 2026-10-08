using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalDiagnosticRetentionPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "diagnostic-retention-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_saved_reset_and_unconfirmed_restart_preserve_unrelated_preferences_and_cleanup()
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        var preferences = new LocalDiagnosticRetentionPreferences(paths);
        preferences.Load().Should().BeNull();
        store.WriteText("audit-retention.txt", "90");
        foreach (var days in new[] { 1, 30, 365 })
        {
            preferences.BeginWrite();
            preferences.Save(new(days));
            preferences.ReadBack().Should().Be(new DiagnosticRetentionDays(days));
            new LocalDiagnosticRetentionPreferences(paths).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            new LocalDiagnosticRetentionPreferences(paths).Load().Should().Be(new DiagnosticRetentionDays(days));
        }
        preferences.BeginWrite();
        preferences.Reset();
        preferences.ReadBack().Should().BeNull();
        preferences.Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        preferences.Load().Should().BeNull();
        store.ReadText("audit-retention.txt").Should().Be("90");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
        preferences.Invoking(item => item.Save(default)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2\n30")]
    [InlineData("1\n30\nextra")]
    [InlineData("1\n")]
    [InlineData("1\n0")]
    [InlineData("1\n366")]
    [InlineData("1\n-1")]
    [InlineData("1\n 1")]
    [InlineData("1\n01")]
    [InlineData("1\n1.0")]
    public void Invalid_saved_format_is_not_default(string content)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("sqlite-diagnostic-retention.txt", content);
        new LocalDiagnosticRetentionPreferences(store).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Malformed_UTF8_is_explicit_for_both_preference_and_unconfirmed_marker(bool marker)
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        store.WriteText("sqlite-diagnostic-retention.txt", "1\n30");
        var name = marker ? "sqlite-diagnostic-retention-unconfirmed.txt" : "sqlite-diagnostic-retention.txt";
        File.WriteAllBytes(Path.Combine(root, "Preferences", name), [0xC3, 0x28]);
        new LocalDiagnosticRetentionPreferences(paths).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Legacy_valid_UTF8_BOM_is_retained_but_UTF16_BOM_never_reinterprets_malformed_UTF8()
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        store.WriteText("sqlite-diagnostic-retention.txt", "1\n30");
        var file = Path.Combine(root, "Preferences", "sqlite-diagnostic-retention.txt");
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("1\n30")]);
        new LocalDiagnosticRetentionPreferences(paths).Load().Should().Be(DiagnosticRetentionDays.Default);
        File.WriteAllBytes(file, [0xFF, 0xFE, 0x31, 0x00]);
        new LocalDiagnosticRetentionPreferences(paths).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
