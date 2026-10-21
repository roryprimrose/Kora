namespace Kora.Application.Hosting;

public sealed partial class NewRequestComposer
{
    internal Func<bool> CaptureUnsentCloseEligibility()
    {
        var expected = ReuseBufferEpoch;
        var control = access.ControlRevision;
        var exact = target;
        return () => !closed && !busy && access.CanInspect && access.CanControl
            && access.ControlRevision == control && ReuseBufferEpoch == expected && target == exact;
    }
}
