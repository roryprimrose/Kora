namespace Kora;

public sealed class CaptionDisplaySnapshot
{
    internal CaptionDisplaySnapshot(long revision, IReadOnlyList<CaptionDisplayObservation> displays)
    {
        Revision = revision;
        Displays = displays;
    }

    internal long Revision { get; }
    internal IReadOnlyList<CaptionDisplayObservation> Displays { get; }
}
