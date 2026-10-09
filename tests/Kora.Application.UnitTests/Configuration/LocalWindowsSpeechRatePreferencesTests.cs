using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalWindowsSpeechRatePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "rate-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_normal_atomic_restart_pending_and_reset_preserve_other_preferences()
    {
        var paths = new Paths(root);
        var preferences = new LocalWindowsSpeechRatePreferences(paths);
        preferences.Load().Should().BeNull();
        var store = new LocalPreferenceStore(paths);
        store.WriteText("speech-provider.txt", "retained");
        foreach (var value in new[] { -10, 0, 10 })
        {
            preferences.BeginWrite();
            preferences.Save(new(value));
            preferences.ReadBack().Should().Be(new WindowsSpeechRate(value));
            FluentActions.Invoking(() => new LocalWindowsSpeechRatePreferences(store).Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            new LocalWindowsSpeechRatePreferences(store).Load().Should().Be(new WindowsSpeechRate(value));
        }
        preferences.BeginWrite();
        preferences.Reset();
        preferences.ReadBack().Should().BeNull();
        FluentActions.Invoking(() => new LocalWindowsSpeechRatePreferences(paths).Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        preferences.Load().Should().BeNull();
        store.ReadText("speech-provider.txt").Should().Be("retained");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2\n0")]
    [InlineData("1\n0\nextra")]
    [InlineData("1\n")]
    [InlineData("1\n-11")]
    [InlineData("1\n11")]
    [InlineData("1\n 1")]
    [InlineData("1\n01")]
    [InlineData("1\n-0")]
    [InlineData("1\n1.0")]
    public void Invalid_format_never_activates_default(string content)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("speech-windows-rate.txt", content);
        FluentActions.Invoking(() => new LocalWindowsSpeechRatePreferences(store).Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
