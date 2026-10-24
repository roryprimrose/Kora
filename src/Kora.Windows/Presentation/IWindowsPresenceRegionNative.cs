namespace Kora.Windows.Presentation;

internal interface IWindowsPresenceRegionNative
{
    void Validate(nint window);
    (int Width, int Height) GetSize(nint window);
    nint CreateRegion(int width, int height);
    void SetRegion(nint window, nint region);
    void DeleteRegion(nint region);
}
