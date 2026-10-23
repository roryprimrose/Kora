using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalSpeechInputPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "speech-input-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExactTupleAndPendingEvidenceSurviveRestartWithoutChangingOtherPreferences()
    {
        var store = new LocalPreferenceStore(new Paths(root));
        var preferences = new LocalSpeechInputPreferences(new Paths(root));
        preferences.Load().Should().BeNull();
        store.WriteText("speech-provider.txt", "output-unchanged");
        var tuple = SpeechInputSelection.Default with { Resource = SpeechInputResource.Gpu, Residency = SpeechInputResidency.KeepLoaded };
        preferences.BeginWrite();
        preferences.Save(tuple);
        new LocalSpeechInputPreferences(store).ReadBack().Should().Be(tuple);
        preferences.Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();
        new LocalSpeechInputPreferences(store).Load().Should().Be(tuple);
        store.ReadText("speech-provider.txt").Should().Be("output-unchanged");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("2\nwindows-activated-v1\nWindowsManaged\nCaptureLifetime")]
    [InlineData("1\nunknown\nWindowsManaged\nCaptureLifetime")]
    [InlineData("1\nwindows-activated-v1\n0\nCaptureLifetime")]
    [InlineData("1\nwindows-activated-v1\nUnknown\nCaptureLifetime")]
    [InlineData("1\nwindows-activated-v1\nWindowsManaged\nUnknown")]
    [InlineData("1\nwindows-activated-v1\nWindowsManaged\n99")]
    [InlineData("1\nwindows-activated-v1\nWindowsManaged\nCaptureLifetime\nextra")]
    public void MalformedTuplesCannotBecomeTheDefault(string contents)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("speech-input-profile.txt", contents);
        new LocalSpeechInputPreferences(store).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}