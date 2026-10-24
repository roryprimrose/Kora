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
    public string Label => CaptionValue is SpeechCaptionValue.Placement placement
        ? Presentation.SettingsText.CaptionPlacement(placement.Value)
        : CaptionValue is { } value ? value.Label : Mode switch
    {
        SpeechTextMode.Off => "Off",
        SpeechTextMode.CurrentUtterance => "Show spoken words",
        SpeechTextMode.CurrentSentence => "Current sentence (requires qualified alignment)",
        _ => throw new InvalidOperationException("A caption mode or option is required."),
    };
    internal long Revision { get; }
    internal Guid Owner { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
}
