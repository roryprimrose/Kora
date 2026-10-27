namespace Kora.Core.Presentation;

public interface IMotionPreferenceSource
{
    MotionPreference Read();

    // Subscribe before reading. Notifications invalidate the last read, not carry a cached value.
    IDisposable Subscribe(Action changed);
}
