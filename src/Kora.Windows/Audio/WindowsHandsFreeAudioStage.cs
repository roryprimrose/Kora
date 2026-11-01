using System.Collections.ObjectModel;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Windows.Audio;

public sealed class WindowsHandsFreeAudioStage : IHandsFreeAudioStage
{
    private readonly ReadOnlyCollection<HandsFreeNativeProfile> profiles;
    private readonly HandsFreeSetupRuntime? setup;
    private readonly IHandsFreeNativeFactory factory;

    public WindowsHandsFreeAudioStage() : this([]) { }
    public WindowsHandsFreeAudioStage(HandsFreeSetupRuntime setup) : this(setup.Profiles) => this.setup = setup;
    internal WindowsHandsFreeAudioStage(IReadOnlyList<HandsFreeNativeProfile> profiles, IHandsFreeNativeFactory? factory = null)
    {
        this.profiles = Array.AsReadOnly(profiles.ToArray());
        this.factory = factory ?? new HandsFreeNativeFactory();
    }
    public bool IsAvailable => setup?.IsPlatformCompatible != false && profiles.Any(profile => profile.CaptureQualified);
    public bool SupportsProfile(string assistantName, SpeechInputSelection selection) =>
        selection == SpeechInputProfileCatalogue.NativeWakeSelection && setup?.IsPlatformCompatible != false
        && profiles.Count(profile => profile.CaptureQualified && profile.Names.Contains(assistantName)) == 1;

    public async Task<IAsyncDisposable> PrepareProfileAsync(
        string assistantName, SpeechInputSelection selection, CancellationToken cancellationToken)
    {
        if (!SupportsProfile(assistantName, selection))
        {
            throw new InvalidOperationException("The exact configured name/profile is unavailable.");
        }
        var profile = profiles.Single(candidate => candidate.CaptureQualified && candidate.Names.Contains(assistantName));
        IAsyncDisposable? ownership = null;
        HandsFreeAssetLease? assets = null;
        try
        {
            if (setup is not null)
            {
                ownership = await setup.TryAcquireOwnershipAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Speech assets remain owned; no replacement is admitted.");
            }
            assets = await profile.BindAsync(cancellationToken, ownership).ConfigureAwait(false);
            ownership = null;
            await PocketSphinxKeywordConfiguration.CreateAsync(profile, assistantName, cancellationToken).ConfigureAwait(false);
            return assets;
        }
        catch
        {
            try { if (assets is not null) { await assets.DisposeAsync().ConfigureAwait(false); } }
            finally { if (ownership is not null) { await ownership.DisposeAsync().ConfigureAwait(false); } }
            throw;
        }
    }

    public async Task<IHandsFreeAudioSession> OpenAsync(HandsFreeAudioBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        binding.Profile.Selection.Validate();
        var profile = profiles.SingleOrDefault(candidate => candidate.CaptureQualified && candidate.Names.Contains(binding.AssistantName));
        if (profile is null || setup?.IsPlatformCompatible == false
            || (binding.Profile.Selection.ProfileId is SpeechInputProfileCatalogue.NativeWakeProfileId
                ? !SupportsProfile(binding.AssistantName, binding.Profile.Selection)
                : !string.Equals(binding.AssistantName, AssistantNameRules.DefaultName, StringComparison.Ordinal)
                    || SpeechInputProfileCatalogue.UnavailableReason(binding.Profile.Selection) is not null))
        {
            throw new InvalidOperationException("The exact local native/model/name/playback profile is unavailable. Use push-to-talk or native controls.");
        }
        bool Admitted() => !cancellationToken.IsCancellationRequested && binding.CanCapture();
        if (!Admitted()) { throw new OperationCanceledException("Fresh hands-free admission is unavailable."); }
        HandsFreeAssetLease? assets = null;
        IAsyncDisposable? ownership = null;
        IHandsFreeKeywordDetector? detector = null;
        IHandsFreeCommandEndpoint? endpoint = null;
        IHandsFreeMicrophone? microphone = null;
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
            var keyword = await PocketSphinxKeywordConfiguration.CreateAsync(profile, binding.AssistantName, cancellationToken).ConfigureAwait(false);
            if (!Admitted()) { throw new OperationCanceledException("Hands-free admission changed during exact keyword preparation."); }
            detector = await Task.Run(() => factory.CreateKeyword(profile, keyword),
                CancellationToken.None).ConfigureAwait(false);
            endpoint = await Task.Run(() => factory.CreateEndpoint(profile),
                CancellationToken.None).ConfigureAwait(false);
            if (!Admitted()) { throw new OperationCanceledException("Hands-free admission changed while loading native resources."); }
            microphone = await factory.OpenMicrophoneAsync(binding, Admitted).ConfigureAwait(false);
            session = new(binding, microphone, detector, endpoint, assets, stream => factory.OpenCommandAsync(binding, stream, Admitted));
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
