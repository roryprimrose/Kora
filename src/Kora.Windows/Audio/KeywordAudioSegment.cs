namespace Kora.Windows.Audio;

internal sealed record KeywordAudioSegment(int FirstFrame, int LastFrame)
{
    public long CommandStartSample
    {
        get
        {
            if (FirstFrame < 0 || LastFrame < FirstFrame)
            {
                throw new InvalidDataException("The native keyword span is invalid.");
            }
            // ps_seg_frames returns an inclusive last frame; the admitted clock is 16 kHz / 100 fps.
            return checked((LastFrame + 1L) * 160);
        }
    }
}
