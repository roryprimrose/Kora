using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed partial class HandsFreeVoiceRecognitionService
{
    private bool profileSwitchActive;
    private bool profileRetirementFailed;
    private IAsyncDisposable? quarantinedProfileAssets;
    private SpeechInputGeneration? selectedProfile;

    public SpeechInputGeneration ProfileGeneration => selectedProfile ?? ProfileRuntime.ProfileGeneration;
    public IReadOnlyList<SpeechInputProfile> GetProfiles(string assistantName) =>
        audio.SupportsProfile(assistantName, SpeechInputProfileCatalogue.NativeWakeSelection)
            ? [.. SpeechInputProfileCatalogue.Profiles, SpeechInputProfileCatalogue.NativeWakeProfile]
            : SpeechInputProfileCatalogue.Profiles;

    public string? GetUnavailableReason(SpeechInputSelection selection, string assistantName)
    {
        selection.Validate();
        return selection == SpeechInputProfileCatalogue.NativeWakeSelection && audio.SupportsProfile(assistantName, selection)
            ? null : SpeechInputProfileCatalogue.UnavailableReason(selection);
    }

    private static SpeechInputSelection? ActivatedSelection(SpeechInputSelection? selection) =>
        selection?.ProfileId is SpeechInputProfileCatalogue.NativeWakeProfileId
            ? selection == SpeechInputProfileCatalogue.NativeWakeSelection ? SpeechInputSelection.Default : null
            : selection;
    public bool IsProfileAvailable
    {
        get { lock (gate) { return !disposed && !profileSwitchActive && !profileRetirementFailed && activated is ISpeechInputProfileRuntime runtime && runtime.IsProfileAvailable; } }
    }

    public void InitializeProfile(SpeechInputSelection? selection)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profileSwitchActive || !IsCaptureQuiescent) { throw new InvalidOperationException("Profile initialization requires unowned quiescent input."); }
            ProfileRuntime.InitializeProfile(ActivatedSelection(selection));
            if (selection is not null) { selectedProfile = new(ProfileRuntime.ProfileGeneration.Number, selection); }
        }
    }

    public async Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profileSwitchActive || quarantinedProfileAssets is not null) { throw new InvalidOperationException("A speech-input switch or unconfirmed resource release owns the runtime."); }
            profileSwitchActive = true;
        }
        try
        {
            await StopAsync(cancellationToken);
            EnsureNotDisposed();
            return new ProfileSwitch(this, await ProfileRuntime.RetireProfileAsync(cancellationToken));
        }
        catch
        {
            lock (gate) { profileSwitchActive = false; profileRetirementFailed = true; }
            throw;
        }
    }

    public async Task<ISpeechInputProfileSwitch> RetireProfileAsync(
        SpeechInputSelection selection, string assistantName, CancellationToken cancellationToken)
    {
        var retirement = await RetireProfileAsync(cancellationToken);
        if (selection.ProfileId is not SpeechInputProfileCatalogue.NativeWakeProfileId) { return retirement; }
        try
        {
            if (GetUnavailableReason(selection, assistantName) is { } unavailable) { throw new InvalidOperationException(unavailable); }
            var assets = await audio.PrepareProfileAsync(assistantName, selection, cancellationToken);
            return new PreparedProfileSwitch(this, retirement, assets, selection, assistantName);
        }
        catch
        {
            retirement.HoldUnavailable();
            await retirement.DisposeAsync();
            throw;
        }
    }

    private ISpeechInputProfileRuntime ProfileRuntime => activated as ISpeechInputProfileRuntime
        ?? throw new InvalidOperationException("The activated adapter does not provide speech-input profile switching.");

    private sealed class ProfileSwitch(HandsFreeVoiceRecognitionService owner, ISpeechInputProfileSwitch native) : ISpeechInputProfileSwitch
    {
        private Task? release;
        public void Publish(SpeechInputGeneration generation)
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(release is not null, this);
                generation.Selection.Validate();
                if (generation.Number != checked(owner.ProfileGeneration.Number + 1))
                {
                    throw new InvalidOperationException("Speech-input publication requires the next exact generation.");
                }
                var activatedSelection = ActivatedSelection(generation.Selection)
                    ?? throw new InvalidOperationException("The native wake resource/residency tuple is unsupported.");
                native.Publish(new(generation.Number, activatedSelection));
                owner.selectedProfile = generation;
                owner.profileRetirementFailed = false;
            }

        }
        public void HoldUnavailable()
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(release is not null, this);
                owner.profileRetirementFailed = true;
                native.HoldUnavailable();
            }

        }
        public ValueTask DisposeAsync()
        {
            lock (owner.gate) { return new(release ??= ReleaseAsync()); }
        }
        private async Task ReleaseAsync()
        {
            await native.DisposeAsync();
            lock (owner.gate) { owner.profileSwitchActive = false; }
        }
    }

    private sealed class PreparedProfileSwitch(HandsFreeVoiceRecognitionService owner, ISpeechInputProfileSwitch retirement,
        IAsyncDisposable assets, SpeechInputSelection selection, string name) : ISpeechInputProfileSwitch
    {
        private Task? release;
        public void Publish(SpeechInputGeneration generation)
        {
            ObjectDisposedException.ThrowIf(release is not null, this);
            if (generation.Selection != selection || owner.GetUnavailableReason(selection, name) is not null)
            {
                throw new InvalidOperationException("The exact prepared name/profile changed before publication.");
            }
            retirement.Publish(generation);
        }
        public void HoldUnavailable() => retirement.HoldUnavailable();
        public ValueTask DisposeAsync()
        {
            lock (owner.gate) { return new(release ??= ReleaseAsync()); }
        }
        private async Task ReleaseAsync()
        {
            try { await assets.DisposeAsync(); }
            catch
            {
                lock (owner.gate) { owner.quarantinedProfileAssets = assets; }
                retirement.HoldUnavailable();
                throw;
            }
            finally { await retirement.DisposeAsync(); }
        }
    }
}
