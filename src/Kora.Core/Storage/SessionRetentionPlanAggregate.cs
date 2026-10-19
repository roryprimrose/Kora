namespace Kora.Core.Storage;

public sealed record SessionRetentionPlanAggregate(int Sessions, int NewlyArchiveDue, int NewlyDeleteDue,
    int RecordedArchiveDue, int RecordedDeleteDue, int Kept, int WorkHeld, int UnknownHold, int Removed);
