namespace Kora.Windows.IntegrationTests.Audio;

internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows native APIs.";
        }
    }
}