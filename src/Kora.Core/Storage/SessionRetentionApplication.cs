using Kora.Core.Configuration;

namespace Kora.Core.Storage;

/// <summary>Domain-owned deadline arithmetic and inclusive due-action selection.</summary>
public static class SessionRetentionApplication
{
    public static SessionRetentionState Recalculate(SessionRetentionState original,
        SessionRetentionSettings settings, DateTimeOffset cutoff)
    {
        ArgumentNullException.ThrowIfNull(original);
        settings.Validate();
        original.SessionId.Validate();
        if (original.LastMeaningfulActivity.Offset != TimeSpan.Zero || cutoff.Offset != TimeSpan.Zero
            || original.LastMeaningfulActivity > cutoff)
        { throw new InvalidDataException("A canonical UTC activity clock no later than the review cutoff is required."); }
        try
        {
            return original with
            {
                ArchiveDue = original.LastMeaningfulActivity.AddDays(settings.ArchiveDays),
                DeleteDue = original.LastMeaningfulActivity.AddDays(settings.DeleteDays),
            };
        }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException("The selected retention deadlines are outside the supported clock range.", exception); }
    }

    public static SessionRetentionApplyEffect Effect(SessionRetentionState state, bool active, DateTimeOffset cutoff) =>
        state.DeleteDue <= cutoff ? SessionRetentionApplyEffect.Delete
        : active && state.ArchiveDue <= cutoff ? SessionRetentionApplyEffect.Archive
        : SessionRetentionApplyEffect.DeadlinesOnly;
}
