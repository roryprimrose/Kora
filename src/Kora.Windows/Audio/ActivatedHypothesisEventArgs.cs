namespace Kora.Windows.Audio;

internal sealed class ActivatedHypothesisEventArgs(string text, long sequence) : EventArgs
{
    public string Text { get; } = text;
    public long Sequence { get; } = sequence;
}
