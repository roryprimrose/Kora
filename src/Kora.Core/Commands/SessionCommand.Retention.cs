using System.Text;

using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string RetentionSyntax =
        "session retention status <session-id> | session retention review <session-id> keep|ordinary | "
        + "session retention confirm <session-id> <review-id> | session retention cancel <session-id> <review-id>. "
        + "Exact immutable canonical lowercase D GUIDs only. Review expires after 60 seconds and is consumed once. "
        + "Confirmation opens the same exact native review; only a genuine new native decision can change the hold. "
        + "Voice requires the current assistant-name prefix. No apply-now cleanup, date renewal, global defaults, "
        + "grants, memory or provider permission. Model retention control is unavailable.";

    public bool KeepRetention { get; init; }
    public Guid? RetentionReviewId { get; init; }

    public bool IsRetentionCommand => Operation is SessionCommandOperation.RetentionStatus
        or SessionCommandOperation.RetentionReview or SessionCommandOperation.RetentionConfirm or SessionCommandOperation.RetentionCancel;

    private static SessionCommand? ParseRetention(string text, string input)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("retention", StringComparison.OrdinalIgnoreCase))
        { return null; }
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: RetentionSyntax);
        if (Encoding.UTF8.GetByteCount(input) > MaximumInputBytes || input.Any(char.IsControl)) { return Invalid(); }
        if (parts.Length == 3 && parts[2].Equals("help", StringComparison.OrdinalIgnoreCase))
        { return new(SessionCommandOperation.Help); }
        if (parts.Length < 4) { return Invalid(); }
        try
        {
            var session = OptionalCanonicalGuid.Parse(parts[3]);
            if (parts.Length == 4 && parts[2].Equals("status", StringComparison.OrdinalIgnoreCase))
            { return new(SessionCommandOperation.RetentionStatus, session); }
            if (parts.Length != 5) { return Invalid(); }
            if (parts[2].Equals("review", StringComparison.OrdinalIgnoreCase))
            {
                if (!parts[4].Equals("keep", StringComparison.OrdinalIgnoreCase)
                    && !parts[4].Equals("ordinary", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
                return new(SessionCommandOperation.RetentionReview, session)
                { KeepRetention = parts[4].Equals("keep", StringComparison.OrdinalIgnoreCase) };
            }
            var review = OptionalCanonicalGuid.Parse(parts[4]);
            if (parts[2].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            { return new(SessionCommandOperation.RetentionConfirm, session) { RetentionReviewId = review }; }
            if (parts[2].Equals("cancel", StringComparison.OrdinalIgnoreCase))
            { return new(SessionCommandOperation.RetentionCancel, session) { RetentionReviewId = review }; }
            return Invalid();
        }
        catch (InvalidDataException) { return Invalid(); }
    }
}
