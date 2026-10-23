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
    public string Disclosure => $"{Operation}: {Component.Name}. Model: {Component.ModelIdentity}. "
        + $"Installed runtime profile: {Component.RuntimeProfile}. Licence: {Component.Licence} ({Component.LicenceSource}). "
        + $"Sources: {string.Join(", ", Component.Assets.Select(asset => asset.Source.AbsoluteUri))}. "
        + $"Download: {(Operation == SpeechComponentOperation.Prepare && !Readiness.Storage.Verified ? Component.DownloadBytes : 0)} bytes; "
        + $"staging: {Component.StagingBytes} bytes; installed: {Component.InstalledBytes} bytes; "
        + $"RAM: {Component.RamBytes} bytes; VRAM: {Component.VramBytes} bytes. "
        + "Destination: this user's private Kora speech-component store, separate from application code and personal profiles. "
        + "Existing unknown/shared data is preserved. Removal deletes model data only. "
        + "No runtime selection, microphone use, learning, enrollment, reasoning or output-setting change is authorized.";
}
