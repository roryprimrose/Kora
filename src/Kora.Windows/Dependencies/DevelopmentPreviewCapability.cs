namespace Kora.Windows.Dependencies;

[Flags]
public enum DevelopmentPreviewCapability
{
    None = 0,
    LocalModel = 1,
    Claude = 2,
    Wake = 4,
    BundledWorker = 8,
}
