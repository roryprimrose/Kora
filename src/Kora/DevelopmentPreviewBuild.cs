namespace Kora;

internal static class DevelopmentPreviewBuild
{
    internal static bool Enabled =>
#if KORA_DEVELOPMENT_PREVIEW
        true;
#else
        false;
#endif
}
