namespace Kora.Windows.Presentation;

internal sealed class WindowsPresenceRegion(nint window, IWindowsPresenceRegionNative native)
{
    private (int Width, int Height)? applied;
    private bool applying;

    internal void Initialize()
    {
        native.Validate(window);
        Apply(false);
    }

    internal bool Apply(bool intercept)
    {
        if (applying) { return false; }
        applying = true;
        try
        {
            var footprint = native.GetSize(window);
            var size = intercept ? footprint : (0, 0);
            if (applied == size) { return true; }
            // A real empty region removes the footprint; NULL would restore the entire window.
            var region = native.CreateRegion(size.Item1, size.Item2);
            try
            {
                native.SetRegion(window, region);
            }
            catch
            {
                native.DeleteRegion(region);
                throw;
            }
            // SetWindowRgn transferred ownership to Windows.
            applied = size;
            return true;
        }
        finally
        {
            applying = false;
        }
    }
}
