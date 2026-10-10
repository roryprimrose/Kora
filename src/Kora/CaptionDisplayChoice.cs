namespace Kora;

public sealed class CaptionDisplayChoice
{
    internal CaptionDisplayChoice(CaptionDisplayObservation display, long revision, long selectionRevision, string label)
    {
        Display = display;
        Revision = revision;
        SelectionRevision = selectionRevision;
        Label = label;
    }

    internal CaptionDisplayObservation Display { get; }
    internal long Revision { get; }
    internal long SelectionRevision { get; }
    public string Label { get; }
    public override string ToString() => Label;
}
