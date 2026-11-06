using System.Text;

using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string PermissionSyntax =
        "session permission status <session-id> | session permission review <session-id> review-all|model-review|approve-all | "
        + "session permission confirm <session-id> | session permission cancel <session-id>. "
        + "Use an exact canonical lowercase D GUID. Review expires after 60 seconds; confirm once through the same original channel. "
        + "Voice requires the current assistant-name prefix and is unavailable during protected calls. "
        + "No grants, execution, provider enablement or model permission control.";

    public SessionPermissionLevel? PermissionLevel { get; init; }
    public bool IsPermissionCommand => Operation is SessionCommandOperation.PermissionStatus
        or SessionCommandOperation.PermissionReview or SessionCommandOperation.PermissionConfirm or SessionCommandOperation.PermissionCancel;

    private static SessionCommand? ParsePermission(string text, string input)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("permission", StringComparison.OrdinalIgnoreCase))
        { return null; }
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: PermissionSyntax);
        if (Encoding.UTF8.GetByteCount(input) > MaximumInputBytes || input.Any(char.IsControl)) { return Invalid(); }
        if (parts.Length == 3 && parts[2].Equals("help", StringComparison.OrdinalIgnoreCase))
        { return new(SessionCommandOperation.Help); }
        if (parts.Length is not (4 or 5)) { return Invalid(); }
        try
        {
            var session = OptionalCanonicalGuid.Parse(parts[3]);
            if (parts.Length == 4)
            {
                return parts[2].ToLowerInvariant() switch
                {
                    "status" => new(SessionCommandOperation.PermissionStatus, session),
                    "confirm" => new(SessionCommandOperation.PermissionConfirm, session),
                    "cancel" => new(SessionCommandOperation.PermissionCancel, session),
                    _ => Invalid(),
                };
            }
            if (!parts[2].Equals("review", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
            var level = parts[4].ToLowerInvariant() switch
            {
                "review-all" => SessionPermissionLevel.ReviewAll,
                "model-review" => SessionPermissionLevel.ModelReview,
                "approve-all" => SessionPermissionLevel.ApproveAll,
                _ => (SessionPermissionLevel?)null,
            };
            return level is null ? Invalid() : new(SessionCommandOperation.PermissionReview, session) { PermissionLevel = level };
        }
        catch (InvalidDataException) { return Invalid(); }
    }
}
