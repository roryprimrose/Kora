using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private QueueCapabilityOption? queueCapability;
    private long queueCapabilityEpoch;
    private long queueCapabilityAdmission;
    public IReadOnlyList<QueueCapabilityOption> QueueCapabilities { get; } =
        ReadOnlyCapabilityCatalog.Descriptors.Select(descriptor => new QueueCapabilityOption(descriptor)).ToArray();
    public QueueCapabilityOption? SelectedQueueCapability => QueueCapabilityCurrent ? queueCapability : null;
    public bool CanSelectQueueCapability => CanControlQueue;
    public bool CanEnqueueCapability => CanEnqueueVersion && QueueCapabilityCurrent;
    public string QueueCapabilityStatus => SelectedQueueCapability is { } option
        ? "Explicit descriptor target: " + option.Label + ". Enqueue clears this choice; no arbitrary tool request."
        : "Choose one exact current catalogue ID for capabilities.get; no implicit or sticky target. "
            + "runtime.get_status always reads the recorded local.inference observation, never probes or runs inference.";
    private bool QueueCapabilityCurrent => queueCapability is not null && !closed && access.CanInspect && access.CanControl
        && queueCapabilityEpoch == selectionEpoch && queueCapabilityAdmission == access.ControlRevision
        && queueSnapshot?.SessionId == selected?.Authority.SessionId && queueSnapshot?.Generation == selected?.Authority.Generation;

    public void SelectQueueCapability(QueueCapabilityOption? option)
    {
        if (notifying || busy || !CanSelectQueueCapability || option is null
            || !QueueCapabilities.Any(item => ReferenceEquals(item, option))) { return; }
        queueCapability = option;
        queueCapabilityEpoch = selectionEpoch;
        queueCapabilityAdmission = access.ControlRevision;
        NotifyQueueCapability();
    }

    public Task EnqueueCapabilityAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueEnqueue,
        SessionQueueReadProfile.Capability, queueCapability is { } option ? new(option.Descriptor.Id) : null);
    public Task EnqueueRuntimeStatusAsync() =>
        RunQueueCommandAsync(SessionCommandOperation.QueueEnqueue, SessionQueueReadProfile.RuntimeStatus);

    private void InvalidateQueueCapability() => queueCapability = null;

    private void NotifyQueueCapability()
    {
        NotifyPreparedQueuePrerequisite();
        if (!QueueCapabilityCurrent) { InvalidateQueueCapability(); }
        OnPropertyChanged(nameof(SelectedQueueCapability));
        OnPropertyChanged(nameof(CanSelectQueueCapability));
        OnPropertyChanged(nameof(CanEnqueueCapability));
        OnPropertyChanged(nameof(QueueCapabilityStatus));
    }
}
