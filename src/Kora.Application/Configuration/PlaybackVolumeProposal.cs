using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class PlaybackVolumeProposal
{
    internal PlaybackVolumeProposal(PlaybackVolume? value, long revision, long callRevision,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible)
    {
        Value = value;
        Revision = revision;
        CallRevision = callRevision;
        Session = session;
        Origin = origin;
        Eligible = eligible;
    }

    public PlaybackVolume? Value { get; }
    internal long Revision { get; }
    internal long CallRevision { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
}
