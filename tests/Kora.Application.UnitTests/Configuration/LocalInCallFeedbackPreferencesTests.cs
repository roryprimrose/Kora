using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Communication;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalInCallFeedbackPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "call-feedback-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Atomic_writes_restart_pending_and_reset_preserve_every_unrelated_preference()
    {
        var paths = new Paths(root);
        var preferences = new LocalInCallFeedbackPreferences(paths);
        preferences.Load().Should().BeNull();
        var store = new LocalPreferenceStore(paths);
        store.WriteText("call-aware-settings.txt", "0,1");
        store.WriteText("voice-consent.txt", "unchanged");
        foreach (var mode in Enum.GetValues<InCallFeedbackMode>())
        {
            preferences.BeginWrite();
            preferences.Save(mode);
            preferences.ReadBack().Should().Be(mode);
            FluentActions.Invoking(() => new LocalInCallFeedbackPreferences(store).Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            new LocalInCallFeedbackPreferences(store).Load().Should().Be(mode);
        }
        preferences.BeginWrite();
        preferences.Reset();
        preferences.ReadBack().Should().BeNull();
        FluentActions.Invoking(() => preferences.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        preferences.Load().Should().BeNull();
        store.ReadText("call-aware-settings.txt").Should().Be("0,1");
        store.ReadText("voice-consent.txt").Should().Be("unchanged");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
        FluentActions.Invoking(() => preferences.Save((InCallFeedbackMode)99)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2\nUI")]
    [InlineData("1\nUI\nextra")]
    [InlineData("1\n")]
    [InlineData("1\nui")]
    [InlineData("1\n UI")]
    [InlineData("1\nUI ")]
    [InlineData("1\n0")]
    public void Corrupt_or_unknown_format_never_activates_default(string contents)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("in-call-feedback.txt", contents);
        FluentActions.Invoking(() => new LocalInCallFeedbackPreferences(store).Load()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Invalid_utf8_and_unknown_pending_marker_refuse()
    {
        var preferences = new LocalInCallFeedbackPreferences(new Paths(root));
        preferences.Save(InCallFeedbackMode.UI);
        File.WriteAllBytes(Path.Combine(root, "Preferences", "in-call-feedback.txt"), [0xff]);
        FluentActions.Invoking(() => preferences.Load()).Should().Throw<InvalidDataException>();
        preferences.Save(InCallFeedbackMode.UI);
        File.WriteAllText(Path.Combine(root, "Preferences", "in-call-feedback-unconfirmed.txt"), "unknown");
        FluentActions.Invoking(() => preferences.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
