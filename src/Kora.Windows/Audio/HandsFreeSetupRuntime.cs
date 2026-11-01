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
    private CaptureOwnership? captureOwner;
    private bool mutationRequested;
    private long consumerRevision;
    private bool captureHeld;
    private bool disposed;
    private readonly SpeechComponentDefinition component;
    private readonly HandsFreeNativeProfile profile;
    private readonly bool compatible;

    public HandsFreeSetupRuntime(ISpeechComponentFiles files)
        : this(HandsFreeSetupCatalogue.Component, HandsFreeNativeProfileCatalogue.CreatePreparedProfile(files),
            OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
    { }

    internal HandsFreeSetupRuntime(SpeechComponentDefinition component, HandsFreeNativeProfile profile, bool compatible)
    {
        this.component = component;
        this.profile = profile;
        this.compatible = compatible;
    }

    internal IReadOnlyList<HandsFreeNativeProfile> Profiles => [profile];
    internal bool IsPlatformCompatible => compatible;
    internal bool IsCaptureHeld { get { lock (ownership) { return captureHeld; } } }
    internal const string CaptureUnavailable = "Hands-free capture is Unavailable: name, playback and acoustic qualification is missing. "
        + "Use PTT, typed or native controls; preparation grants no capture/inference authority.";

    public SpeechComponentRuntimeObservation Observe(SpeechComponentDefinition component)
    {
        RequireIdentity(component);
        var nativeIdentity = compatible ? profile.ObserveNativeIdentity() : null;
        lock (ownership)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new(compatible ? SpeechComponentCompatibility.Compatible : SpeechComponentCompatibility.Incompatible,
                SpeechComponentFunctionalEvidence.NotChecked,
                "Windows x64 CPU only. Native/data binding is NotChecked. Review Verify; for corrupt owned data, Remove then Prepare. "
                + "Preserve unknown/shared data. A bundled-native mismatch needs application repair, not another model download. "
                + CaptureUnavailable)
            {
                NativeIdentity = nativeIdentity,
                ConsumerRevision = consumerRevision,
                ActiveConsumers = captureOwner is null ? 0 : 1,
                ConsumersKnown = captureOwner?.Unconfirmed != true,
                Dependents = captureOwner is null ? "None; no loaded model or capture uses these data."
                    : "This profile's owned hands-free open/capture, command transcription and model resources.",
            };
        }
    }

    public void HoldUnavailable(SpeechComponentDefinition component)
    {
        RequireIdentity(component);
        lock (ownership)
        {
            if (currentOwner is null || captureOwner is not null)
            { throw new InvalidOperationException("Holding speech assets unavailable requires exclusive joined ownership."); }
            captureHeld = true;
        }
    }

    public async ValueTask<SpeechComponentMutationAdmission> TryAcquireExclusiveAsync(
        SpeechComponentDefinition component, CancellationToken cancellationToken)
    {
        RequireIdentity(component);
        if (!compatible) { return new(SpeechComponentMutationState.Unavailable); }
        cancellationToken.ThrowIfCancellationRequested();
        CaptureOwnership? consumer;
        lock (ownership)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (mutationRequested || currentOwner is not null) { return new(SpeechComponentMutationState.Busy); }
            mutationRequested = true;
            consumer = captureOwner;
        }
        try
        {
            if (consumer is not null) { await consumer.RetireAsync().ConfigureAwait(false); }
            cancellationToken.ThrowIfCancellationRequested();
            lock (ownership)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                currentOwner = new(this);
                return new(SpeechComponentMutationState.Acquired, currentOwner);
            }
        }
        finally
        {
            lock (ownership) { mutationRequested = false; }
        }
    }

    internal ValueTask<CaptureOwnership?> TryAcquireOwnershipAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (ownership)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (captureHeld || mutationRequested || currentOwner is not null || captureOwner is not null)
            { return ValueTask.FromResult<CaptureOwnership?>(null); }
            consumerRevision = checked(consumerRevision + 1);
            captureOwner = new(this);
            return ValueTask.FromResult<CaptureOwnership?>(captureOwner);
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
            if (captureOwner is { } capture) { return new(capture.RetireAsync()); }
            return currentOwner is { } owner ? new(owner.Joined.Task) : ValueTask.CompletedTask;
        }
    }

    internal sealed class CaptureOwnership : IAsyncDisposable
    {
        private readonly HandsFreeSetupRuntime runtime;
        private readonly CancellationTokenSource retirement = new();
        private readonly TaskCompletionSource joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<Task>? retireConsumer;
        private bool retired;
        private bool unconfirmed;
        internal bool Unconfirmed => Volatile.Read(ref unconfirmed);
        private Task? retiring;
        private readonly CancellationToken token;
        internal CaptureOwnership(HandsFreeSetupRuntime runtime)
        {
            this.runtime = runtime;
            token = retirement.Token;
        }
        internal CancellationToken Token => token;

        internal bool Attach(Func<Task> retire)
        {
            lock (runtime.ownership)
            {
                if (retired) { return false; }
                retireConsumer = retire;
                return true;
            }
        }

        internal Task RetireAsync()
        {
            lock (runtime.ownership) { return retiring ??= RetireCoreAsync(); }
        }

        [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks",
            Justification = "Removal must join the exact owned capture/open task, even when cancellation is ignored.")]
        private async Task RetireCoreAsync()
        {
            Func<Task>? retire;
            Task cancellation;
            lock (runtime.ownership)
            {
                retired = true;
                retire = retireConsumer;
                cancellation = joined.Task.IsCompleted ? Task.CompletedTask : retirement.CancelAsync();
            }
            await cancellation.ConfigureAwait(false);
            if (retire is not null) { await retire().ConfigureAwait(false); }
            await joined.Task.ConfigureAwait(false);
            if (Unconfirmed)
            { throw new InvalidOperationException("Owned speech resources did not confirm quiescence. Assets remain held Unavailable."); }
            retirement.Dispose();
        }

        internal void Fail()
        {
            lock (runtime.ownership)
            {
                retired = true;
                unconfirmed = true;
                joined.TrySetResult();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (runtime.ownership)
            {
                if (!joined.Task.IsCompleted)
                {
                    runtime.captureOwner = null;
                    joined.SetResult();
                    if (!retired) { retirement.Dispose(); }
                }
            }
            return ValueTask.CompletedTask;
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