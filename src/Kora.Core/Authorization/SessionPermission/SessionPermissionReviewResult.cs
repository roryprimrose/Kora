namespace Kora.Core.Authorization.SessionPermission;

public sealed record SessionPermissionReviewResult(
    Guid RequestId, SessionPermissionVerdict Verdict, string ProviderVersion, string ReasonCode)
{
    public bool IsValidFor(SessionPermissionReviewRequest request) =>
        RequestId == request.Id && Enum.IsDefined(Verdict)
        && IsBoundedLabel(ProviderVersion) && IsBoundedLabel(ReasonCode);

    private static bool IsBoundedLabel(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 80 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
