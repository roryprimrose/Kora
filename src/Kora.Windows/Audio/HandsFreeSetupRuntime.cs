using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace Kora.Windows.Audio;

/// <summary>Owns preparation and capture exclusion; verifies inert asset binding without loading native code.</summary>
public sealed class HandsFreeSetupRuntime : ISpeechComponentRuntime, IAsyncDisposable
{
    private readonly Lock ownership = new();
    private OwnershipLease? currentOwner;
    private bool disposed;
    private readonly SpeechComponentDefinition component;
    private readonly HandsFreeNativeProfile profile;
    private readonly bool compatible;

    public HandsFreeSetupRuntime(ISpeechComponentFiles files)
        : this(HandsFreeSetupCatalogue.Component, HandsFreeNativeProfileCatalogue.CreatePreparedProfile(files),
            OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64) { }

    internal HandsFreeSetupRuntime(SpeechComponentDefinition component, HandsFreeNativeProfile profile, bool compatible)
    {
        this.component = component;
        this.profile = profile;
        this.compatible = compatible;
    }

    internal IReadOnlyList<HandsFreeNativeProfile> Profiles => [profile];
    internal bool IsPlatformCompatible => compatible;
    internal const string CaptureUnavailable = "Hands-free capture is Unavailable: name, playback and acoustic qualification is missing. "
        + "Use PTT, typed or native controls; preparation grants no capture/inference authority.";

    public SpeechComponentRuntimeObservation Observe(SpeechComponentDefinition component)
    {
        RequireIdentity(component);
        return new(compatible ? SpeechComponentCompatibility.Compatible : SpeechComponentCompatibility.Incompatible,
            SpeechComponentFunctionalEvidence.NotChecked,
            "Windows x64 CPU only. Native/data binding is NotChecked. Review Verify; for corrupt owned data, Remove then Prepare. "
            + "Preserve unknown/shared data. A bundled-native mismatch needs application repair, not another model download. "
            + CaptureUnavailable);
    }

    public async ValueTask<SpeechComponentMutationAdmission> TryAcquireExclusiveAsync(
        SpeechComponentDefinition component, CancellationToken cancellationToken)
    {
        RequireIdentity(component);
        if (!compatible) { return new(SpeechComponentMutationState.Unavailable); }
        var lease = await TryAcquireOwnershipAsync(cancellationToken).ConfigureAwait(false);
        return lease is null ? new(SpeechComponentMutationState.Busy) : new(SpeechComponentMutationState.Acquired, lease);
    }

    internal ValueTask<IAsyncDisposable?> TryAcquireOwnershipAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (ownership)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (currentOwner is not null) { return ValueTask.FromResult<IAsyncDisposable?>(null); }
            currentOwner = new(this);
            return ValueTask.FromResult<IAsyncDisposable?>(currentOwner);
        }
    }

    public async Task<SpeechComponentRuntimeObservation> VerifyAsync(
        SpeechComponentDefinition component, CancellationToken cancellationToken)
    {
        RequireIdentity(component);
        if (!compatible) { return Observe(component); }
        Task<HandsFreeAssetLease> binding;
        lock (ownership)
        {
            var lease = currentOwner ?? throw new InvalidOperationException("Inert verification requires exclusive preparation ownership.");
            binding = lease.BeginBinding(profile, cancellationToken);
        }
        await binding.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(SpeechComponentCompatibility.Compatible, SpeechComponentFunctionalEvidence.Verified,
            "Prepared: the exact complete native/data/notice/ownership inventory passed the production asset binder. "
            + "No native library or model was executed. This is asset preparation evidence, not speech accuracy or capture readiness. "
            + CaptureUnavailable);
    }

    private void RequireIdentity(SpeechComponentDefinition candidate)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        if (!ReferenceEquals(candidate, component))
        {
            throw new InvalidOperationException("The exact host-authored hands-free preparation offer is required. Review this release's catalogue.");
        }

    }

    public ValueTask DisposeAsync()
    {
        lock (ownership)
        {
            disposed = true;
            return currentOwner is { } owner ? new(owner.Joined.Task) : ValueTask.CompletedTask;
        }
    }

    private sealed class OwnershipLease(HandsFreeSetupRuntime runtime) : IAsyncDisposable
    {
        private Task<HandsFreeAssetLease>? binding;
        private Task? release;
        internal TaskCompletionSource Joined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<HandsFreeAssetLease> BeginBinding(HandsFreeNativeProfile candidate, CancellationToken cancellationToken)
        {
            if (binding is not null || release is not null)
            {
                throw new InvalidOperationException("This ownership has already verified or is retiring. Inspect and review again.");
            }
            return binding = candidate.BindAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            lock (runtime.ownership) { return new(release ??= ReleaseAsync()); }
        }

        [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks",
            Justification = "Resource retirement must join the exact asset-binding worker started by this owned lease.")]
        private async Task ReleaseAsync()
        {
            if (binding is not null)
            {
                HandsFreeAssetLease assets;
                try { assets = await binding.ConfigureAwait(false); }
                catch
                {
                    // BindAsync has joined and closed its partial inventory before reporting failure.
                    Relinquish();
                    throw;
                }
                await assets.DisposeAsync().ConfigureAwait(false);
            }
            Relinquish();
        }

        private void Relinquish()
        {
            lock (runtime.ownership)
            {
                runtime.currentOwner = null;
                Joined.TrySetResult();
            }
        }
    }
}
