namespace Kora.Windows.Audio;

internal static class HandsFreeNativeProfileCatalogue
{
    // Release admission supplies exact payload digests, names and playback qualification here.
    // No such payload/profile is currently admitted; installed/fake platform availability is not approval.
    public static IReadOnlyList<HandsFreeNativeProfile> Production { get; } = [];
}
