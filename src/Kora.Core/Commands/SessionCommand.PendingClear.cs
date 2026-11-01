using System.Text;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string PendingClearSyntax =
        "session pending clear <session-id> | session pending confirm <session-id> <review-id> | "
        + "session pending cancel <session-id> <review-id>. "
        + "Clear queue reviews ALL current ordinary never-dispatched pending requests in ONE explicit Active conversation. "
        + "Separate confirmation clears ONLY that exact reviewed set, preserving running work and retained history. "
        + "No diagnostic queue command, new scheduling authority or model control. Voice requires the current assistant-name prefix.";

    public Guid? PendingClearReviewId { get; init; }
    public bool IsPendingClear => Operation is SessionCommandOperation.PendingClearReview
        or SessionCommandOperation.PendingClearConfirm or SessionCommandOperation.PendingClearCancel;

    private static SessionCommand? ParsePendingClear(string text)
    {
        if (!text.Equals("session pending", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("session pending ", StringComparison.OrdinalIgnoreCase))
        { return null; }
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Encoding.UTF8.GetByteCount(text) <= MaximumInputBytes && !text.Any(char.IsControl)
            && parts.Length >= 4 && ExactId(parts[3], out var session))
        {
            if (parts.Length == 4 && parts[2].Equals("clear", StringComparison.OrdinalIgnoreCase))
            { return new(SessionCommandOperation.PendingClearReview, session); }
            if (parts.Length == 5 && ExactId(parts[4], out var review)
                && parts[2].ToLowerInvariant() is "confirm" or "cancel")
            {
                return new(parts[2].Equals("confirm", StringComparison.OrdinalIgnoreCase)
                    ? SessionCommandOperation.PendingClearConfirm : SessionCommandOperation.PendingClearCancel, session)
                { PendingClearReviewId = review };
            }
        }
        return new(SessionCommandOperation.Invalid, Error: PendingClearSyntax);
    }
}
