namespace Kora.Windows.Audio;

internal interface IHandsFreeKeywordDetector : IDisposable
{
    KeywordAudioSegment? Accept(ReadOnlySpan<short> audio);
}
