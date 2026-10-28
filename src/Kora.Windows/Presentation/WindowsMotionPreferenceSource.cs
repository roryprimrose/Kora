using System.Runtime.InteropServices;

using Kora.Core.Presentation;

using Microsoft.Win32;

namespace Kora.Windows.Presentation;

public sealed class WindowsMotionPreferenceSource : IMotionPreferenceSource
{
    private readonly IWindowsMotionPreferenceNative native;

    public WindowsMotionPreferenceSource() : this(new WindowsMotionPreferenceNative()) { }

    internal WindowsMotionPreferenceSource(IWindowsMotionPreferenceNative native) => this.native = native;

    public MotionPreference Read()
    {
        try
        {
            return native.IsAnimationEnabled() ? MotionPreference.AnimationsEnabled : MotionPreference.ReducedMotion;
        }
        catch (ExternalException exception)
        {
            throw new InvalidOperationException("The Windows animation preference could not be read.", exception);
        }
    }

    public IDisposable Subscribe(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        try
        {
            return native.Subscribe(changed);
        }
        catch (ExternalException exception)
        {
            throw new InvalidOperationException("Windows animation preference notifications are unavailable.", exception);
        }
    }

    private sealed class WindowsMotionPreferenceNative : IWindowsMotionPreferenceNative
    {
        public bool IsAnimationEnabled() => WindowsPresenceMotionPreference.IsAnimationEnabled();

        public IDisposable Subscribe(Action changed) => new Subscription(changed);

        private sealed class Subscription : IDisposable
        {
            private readonly Action changed;
            private int disposed;

            public Subscription(Action changed)
            {
                this.changed = changed;
                SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
            }

            private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
            {
                if (Volatile.Read(ref disposed) == 0) { changed(); }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
                }
            }
        }
    }
}
