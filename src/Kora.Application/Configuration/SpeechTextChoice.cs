using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class SpeechTextChoice
{
    internal SpeechTextChoice(SpeechTextMode? mode, long revision, Guid owner,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible, SpeechCaptionValue? captionValue = null)
    {
        Mode = mode;
        Revision = revision;
        Owner = owner;
        Session = session;
        Origin = origin;
        Eligible = eligible;
        CaptionValue = captionValue;
    }
    public SpeechTextMode? Mode { get; }
    public SpeechCaptionValue? CaptionValue { get; }
    public string Label => CaptionValue is { } value ? value.Label : Mode!.Value.ToString();
    internal long Revision { get; }
    internal Guid Owner { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
}
