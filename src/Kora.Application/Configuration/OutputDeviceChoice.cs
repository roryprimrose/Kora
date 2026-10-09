using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class OutputDeviceChoice
{
    internal OutputDeviceChoice(AudioOutputDevice device, long revision, Guid owner, WorkSessionAuthorization session,
        RequestOrigin origin, Func<bool> remainsAdmitted)
    {
        Device = device;
        Revision = revision;
        Owner = owner;
        Session = session;
        Origin = origin;
        RemainsAdmitted = remainsAdmitted;
    }

    public AudioOutputDevice Device { get; }
    public string Id => Device.Id;
    public string Label => Device.Name + " [" + Device.Id + "]";
    public long Revision { get; }
    internal Guid Owner { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> RemainsAdmitted { get; }
}
