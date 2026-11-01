using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

public sealed class SpeechComponentSetupPlan
{
    private int consumed;

    internal SpeechComponentSetupPlan(Guid owner, SpeechComponentDefinition component, SpeechComponentOperation operation,
        SpeechComponentReadiness readiness, HostRequest request, Func<bool> admitted, DateTimeOffset expires)
    {
        Owner = owner;
        Component = component;
        Operation = operation;
        Readiness = readiness;
        ExpectedStorage = readiness.Storage
            ?? throw new ArgumentException("Unknown local data cannot be approved for mutation.", nameof(readiness));
        Request = request;
        Admitted = admitted;
        Expires = expires;
    }

    internal Guid Owner { get; }
    internal HostRequest Request { get; }
    internal Func<bool> Admitted { get; }
    internal DateTimeOffset Expires { get; }
    internal bool Consume() => Interlocked.Exchange(ref consumed, 1) == 0;
    public SpeechComponentDefinition Component { get; }
    public SpeechComponentOperation Operation { get; }
    public SpeechComponentReadiness Readiness { get; }
    public SpeechComponentSnapshot ExpectedStorage { get; }
    public SpeechComponentReadiness? RemovalRecovery { get; internal set; }
    public string Disclosure => $"{Operation}: {Component.Name}. Model: {Component.ModelIdentity}. "
        + $"Installed runtime profile: {Component.RuntimeProfile}. Licence: {Component.Licence} ({Component.LicenceSource}). "
        + $"Sources: {string.Join(", ", Component.Assets.Select(asset => asset.Source.AbsoluteUri))}. "
        + $"Download: {(Operation == SpeechComponentOperation.Prepare && !ExpectedStorage.Verified ? Component.DownloadBytes : 0)} bytes; "
        + $"staging: {Component.StagingBytes} bytes; installed: {Component.InstalledBytes} bytes; "
        + $"RAM: {Component.RamBytes} bytes; VRAM: {Component.VramBytes} bytes. "
        + "Destination: this user's private Kora speech-component store, separate from application code and personal profiles. "
        + "Existing unknown/shared data is preserved. Removal deletes model data only. "
        + "No runtime selection, microphone use, learning, enrollment, reasoning or output-setting change is authorized.";

    public string RemovalDisclosure => $"Remove only {Component.Name}, revision {Component.Identity.Revision}. "
        + $"Current ownership: {ExpectedStorage.Ownership}. Exact remaining inventoried bytes: {ExpectedStorage.ExactBytes}. "
        + $"Current owned asset consumers: {Readiness.Runtime.ActiveConsumers}. Dependents: {Readiness.Runtime.Dependents}; new consumers are excluded during retirement and deletion. "
        + "Only owned capture, transcription and model work using this inventory will be retired and physically joined. "
        + "Unconfirmed quiescence prevents deletion. No other profile or process is stopped.\n"
        + ExpectedStorage.Inventory + "\n"
        + "Saved speech choices are preserved; an affected choice remains Unavailable, without Windows/other-model fallback. "
        + "These preparation assets are not a selectable qualified capture profile. Enablement and acquisition remain separate. "
        + $"Ongoing resource impact: no model is started; RAM planning budget {Component.RamBytes} bytes, VRAM {Component.VramBytes} bytes, not measured release guarantees.\n"
        + "Independent privacy and consent: this does not delete biometric profiles, learning data, recordings, history or retention records; "
        + "does not grant microphone, learning, provider, download or future enablement consent; "
        + "and does not uninstall Kora, Windows, .NET, VC++, native code, shared runtimes or user originals. "
        + "Partial I/O failure leaves the exact remainder Unavailable for fresh inspection and separate confirmation; "
        + "there is no rollback, automatic retry or reacquisition, or claim of physically released/discarded bytes. "
        + "Confirm removal separately from inspection and preparation.";
}