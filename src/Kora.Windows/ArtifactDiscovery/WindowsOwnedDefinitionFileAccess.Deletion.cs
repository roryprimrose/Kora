using Kora.Windows.Coordination;

namespace Kora.Windows.Artifacts;

internal sealed partial class WindowsOwnedDefinitionFileAccess
{
    internal static void DeleteReviewedDefinitionFile(string path, Action finalComparison,
        Func<bool> eligible, Action committed, CancellationToken token)
    {
        using var target = OpenFile(path, 0x80010000, 1);
        VerifyFile(target, path);
        if (!string.Equals(Path.GetFileName(CoordinationNative.CanonicalPath(target)), Path.GetFileName(path), StringComparison.Ordinal))
        { throw new InvalidDataException("Deletion cannot target a filename alias."); }
        // Deny writes and replacement until the reviewed native handle closes.
        finalComparison();
        RequireEligible(eligible, token);
        if (!SetFileInformationByHandle(target, 4, [1], 1))
        { throw NativeFailure("The reviewed source deletion could not be committed."); }
        committed();
    }
}
