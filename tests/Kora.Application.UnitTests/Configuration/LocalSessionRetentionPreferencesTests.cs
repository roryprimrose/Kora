using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalSessionRetentionPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "session-retention-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Atomic_roundtrip_pending_marker_reset_and_unrelated_preferences_are_independent()
    {
        var store = new LocalPreferenceStore(new Paths(root));
        var preferences = new LocalSessionRetentionPreferences(store);
        preferences.Load().Should().BeNull();
        store.WriteText("sqlite-diagnostic-retention.txt", "1\n90");
        foreach (var settings in new[] { new SessionRetentionSettings(1, 2), new(1, 30), new(364, 365) })
        {
            preferences.BeginWrite();
            preferences.Save(settings);
            preferences.ReadBack().Should().Be(settings);
            preferences.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            new LocalSessionRetentionPreferences(new Paths(root)).Load().Should().Be(settings);
        }
        preferences.BeginWrite();
        preferences.Reset();
        preferences.ReadBack().Should().BeNull();
        preferences.ConfirmWrite();
        preferences.Load().Should().BeNull();
        store.ReadText("sqlite-diagnostic-retention.txt").Should().Be("1\n90");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
        preferences.Invoking(value => value.Save(default)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2\n1\n30")]
    [InlineData("1\n1")]
    [InlineData("1\n1\n30\nextra")]
    [InlineData("1\n0\n30")]
    [InlineData("1\n30\n30")]
    [InlineData("1\n31\n30")]
    [InlineData("1\n1\n366")]
    [InlineData("1\n01\n30")]
    [InlineData("1\n1\n 30")]
    public void Invalid_persisted_policy_is_not_a_default(string content)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("session-retention.txt", content);
        new LocalSessionRetentionPreferences(store).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Malformed_UTF8_preference_or_marker_is_rejected(bool marker)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("session-retention.txt", "1\n1\n30");
        File.WriteAllBytes(Path.Combine(root, "Preferences", marker ? "session-retention-unconfirmed.txt" : "session-retention.txt"), [0xC3, 0x28]);
        new LocalSessionRetentionPreferences(store).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }

    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
