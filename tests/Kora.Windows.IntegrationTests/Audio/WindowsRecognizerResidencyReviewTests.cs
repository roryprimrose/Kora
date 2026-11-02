using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.NativeUxFixture;
using Kora.Windows.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Audio;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class WindowsRecognizerResidencyReviewTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("voice")]
    public async Task ExactReviewedTuplePersistsAndCutsOverWithoutOpeningCaptureOrChangingOriginalChannel(string route)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = new DirectoryInfo(AppContext.BaseDirectory);
            while (source is not null && !File.Exists(Path.Combine(source.FullName, "Kora.slnx"))) { source = source.Parent; }
            var scratch = Path.Combine(source?.FullName ?? throw new InvalidOperationException("Owning source root unavailable."),
                ".scratch", "orchestration", "windows-asr-residency", "fixtures");
            Directory.CreateDirectory(scratch);
            using var host = new NativeUxFixtureSession(scratch, syntheticVoice: true);
            var native = new WindowsActivatedCaptureResidencyTests.Native();
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            await using var voice = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, new WindowsActivatedCaptureFactory(native), VoiceCaptureLimits.Default);
            var preferences = new LocalSpeechInputPreferences(host);
            await using var admission = new AudioControlAdmission(host.Interactions, host.Interactions, new(host.Tasks));
            await using var configuration = new SpeechInputConfigurationService(preferences, voice, admission, host.Audit,
                NullLogger<SpeechInputConfigurationService>.Instance);
            await host.InitializeAsync(null, speechInputConfiguration: configuration);
            try
            {
                host.Main.BindVoiceOwnershipGate(static () => true);
                host.Main.BindSpeechInputNativeLifetime(static () => true);
                var selection = SpeechInputSelection.Default with { Residency = SpeechInputResidency.KeepLoaded };
                host.Main.SpeechInputResidencies.Should().Contain(SpeechInputResidency.KeepLoaded);
                if (route is "native")
                {
                    host.Main.SelectedSpeechInputResidency = SpeechInputResidency.KeepLoaded;
                    await host.Main.SaveSpeechInputCommand.ExecuteAsync();
                }
                else if (route is "typed")
                {
                    host.Main.CommandText = "set speech.input to windows-activated-v1 WindowsManaged KeepLoaded";
                    await host.Main.RunTypedCommand.ExecuteAsync();
                }
                else
                {
                    await host.Main.SetVoiceConsentAsync(true);
                    if (!host.Main.IsVoiceEnabled) { await host.Main.ToggleListeningCommand.ExecuteAsync(); }
                    await host.Main.BeginPushToTalkCommand.ExecuteAsync();
                    await host.Main.EndPushToTalkCommand.ExecuteAsync();
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    host.Main.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName is nameof(host.Main.HasSpeechInputReview) && host.Main.HasSpeechInputReview)
                        { ready.TrySetResult(); }
                    };
                    host.Voice.PublishTranscript("Kora, set speech.input to windows-activated-v1 WindowsManaged KeepLoaded");
                    await ready.Task.WaitAsync(TestContext.Current.CancellationToken);
                }
                host.Main.HasSpeechInputReview.Should().BeTrue();
                configuration.Get().Saved.Should().BeNull();
                configuration.Get().Desired.Should().Be(SpeechInputSelection.Default);
                native.Engines.Should().BeEmpty();
                host.Main.SpeechInputReviewStatus.Should().Contain("KeepLoaded");
                await host.Main.ConfirmSpeechInputCommand.ExecuteAsync();
                if (route is "voice")
                {
                    configuration.Get().Saved.Should().BeNull("native confirmation cannot relabel a voice request");
                    host.Main.HasSpeechInputReview.Should().BeTrue();
                    using var confirmation = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
                        HostActivityLayer.Application, HostOperation.Request);
                    await host.Main.ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set, ReviewDecision: true),
                        SecurityAuditInitiator.VoiceCommand);
                    confirmation.Complete(HostOperationOutcome.Completed);
                }
                configuration.Get().Saved.Should().Be(selection);
                configuration.Get().Desired.Should().Be(selection);
                configuration.Get().Effective.Should().Be(new SpeechInputGeneration(1, selection));
                preferences.Load().Should().Be(selection);
                voice.IsListening.Should().BeFalse();
                native.Engines.Should().BeEmpty();
                await voice.BeginPushToTalkAsync(new("mic", "Fixture microphone"), ["help"],
                    cancellationToken: TestContext.Current.CancellationToken);
                await voice.EndPushToTalkAsync(TestContext.Current.CancellationToken);
                native.Engines.Should().ContainSingle();
                var old = native.Engines.Single();
                host.Main.SelectedSpeechInputResidency = SpeechInputResidency.CaptureLifetime;
                await host.Main.SaveSpeechInputCommand.ExecuteAsync();
                old.DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var confirming = host.Main.ConfirmSpeechInputCommand.ExecuteAsync();
                await old.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                confirming.IsCompleted.Should().BeFalse();
                preferences.Load().Should().Be(selection, "persistence follows the physical model join");
                old.DisposeGate.SetResult();
                await confirming;
                preferences.Load().Should().Be(SpeechInputSelection.Default);
                configuration.Get().Effective.Should().Be(new SpeechInputGeneration(2, SpeechInputSelection.Default));
                native.Engines.Should().ContainSingle();
                native.Microphones.Should().ContainSingle();
                voice.IsListening.Should().BeFalse();
            }
            finally
            {
                foreach (var engine in native.Engines) { engine.DisposeGate?.TrySetResult(); }
                host.Main.Dispose();
                await voice.StopAsync(TestContext.Current.CancellationToken);
            }
        });
    }
}
