using System.Collections.ObjectModel;
using Kora.Core.Voice;

namespace Kora.Windows.Audio;

public sealed class WindowsHandsFreeAudioStage : IHandsFreeAudioStage
{
    private readonly ReadOnlyCollection<HandsFreeNativeProfile> profiles;
    private readonly HandsFreeSetupRuntime? setup;

    public WindowsHandsFreeAudioStage() : this([]) { }
    public WindowsHandsFreeAudioStage(HandsFreeSetupRuntime setup) : this(setup.Profiles) => this.setup = setup;
    internal WindowsHandsFreeAudioStage(IReadOnlyList<HandsFreeNativeProfile> profiles) =>
        this.profiles = Array.AsReadOnly(profiles.ToArray());
    public bool IsAvailable => setup?.IsPlatformCompatible != false && profiles.Any(profile => profile.CaptureQualified);

    public async Task<IHandsFreeAudioSession> OpenAsync(HandsFreeAudioBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        var profile = profiles.SingleOrDefault(candidate => candidate.CaptureQualified && candidate.Names.Contains(binding.AssistantName));
        if (profile is null || setup?.IsPlatformCompatible == false
            || SpeechInputProfileCatalogue.UnavailableReason(binding.Profile.Selection) is not null)
        {
            throw new InvalidOperationException("The exact local native/model/name/playback profile is unavailable. Use push-to-talk or native controls.");
        }
        bool Admitted() => !cancellationToken.IsCancellationRequested && binding.CanCapture();
        if (!Admitted()) { throw new OperationCanceledException("Fresh hands-free admission is unavailable."); }
        HandsFreeAssetLease? assets = null;
        IAsyncDisposable? ownership = null;
        PocketSphinxKeywordDetector? detector = null;
        SileroCommandEndpoint? endpoint = null;
        HandsFreeMicrophone? microphone = null;
        WindowsHandsFreeAudioSession? session = null;
        try
        {
            if (setup is not null)
            {
                ownership = await setup.TryAcquireOwnershipAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Speech assets are owned by setup or another capture. Wait for release, then explicitly retry.");
            }
            assets = await profile.BindAsync(cancellationToken, ownership).ConfigureAwait(false);
            ownership = null;
            detector = await Task.Run(() => new PocketSphinxKeywordDetector(profile, binding.AssistantName),
                CancellationToken.None).ConfigureAwait(false);
            endpoint = await Task.Run(() => new SileroCommandEndpoint(profile.VadPath),
                CancellationToken.None).ConfigureAwait(false);
            if (!Admitted()) { throw new OperationCanceledException("Hands-free admission changed while loading native resources."); }
            var opened = await WindowsActivatedCaptureFactory.OpenMicrophoneAsync(binding.Microphone, Admitted).ConfigureAwait(false);
            microphone = new HandsFreeMicrophone(opened.Recorder, opened.Endpoint);
            session = new(binding, microphone, detector, endpoint, assets, stream => Task.Run(
                () => WindowsActivatedCaptureFactory.OpenCommand(binding.Microphone, binding.CommandPhrases, stream, Admitted),
                CancellationToken.None));
            if (!Admitted()) { throw new OperationCanceledException("Hands-free admission changed before native start."); }
            session.Start();
            if (!Admitted()) { throw new OperationCanceledException("Hands-free admission changed during native start."); }
            return session;
        }
        catch
        {
            if (session is not null) { await session.DisposeAsync().ConfigureAwait(false); }
            else
            {
                try { if (microphone is not null) { await microphone.DisposeAsync().ConfigureAwait(false); } }
                finally
                {
                    try { detector?.Dispose(); }
                    finally
                    {
                        try { endpoint?.Dispose(); }
                        finally
                        {
                            try { if (assets is not null) { await assets.DisposeAsync().ConfigureAwait(false); } }
                            finally { if (ownership is not null) { await ownership.DisposeAsync().ConfigureAwait(false); } }
                        }
                    }
                }
            }
            throw;
        }
    }
}
