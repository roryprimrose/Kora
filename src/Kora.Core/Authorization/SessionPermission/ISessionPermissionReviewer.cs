namespace Kora.Core.Authorization.SessionPermission;

/// <summary>
/// A separately admitted, local-only review transport. Implementations own inference/context consent,
/// bounded input/output, cancellation and actual transport quiescence. No executor is an implementation.
/// An unqualified transport is unavailable, not permission to select another profile.
/// </summary>
public interface ISessionPermissionReviewer
{
    bool IsQualified { get; }
    ValueTask<SessionPermissionReviewResult> ReviewAsync(
        SessionPermissionReviewRequest request, CancellationToken cancellationToken);
}
