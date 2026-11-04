using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalQuietHoursPreferences : IQuietHoursPreferences
{
    private const string FileName = "notices-quiet-hours.txt";
    private const string PendingFileName = "notices-quiet-hours-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalQuietHoursPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalQuietHoursPreferences(IPreferenceStore store) => this.store = store;

    public QuietHoursSchedule? Load()
    {
        if (store.ReadText(PendingFileName, 16) is not null)
        { throw new InvalidDataException("A quiet-hours write is unconfirmed. Inspect saved intent and audit receipts, explicitly repair, then inspect again."); }
        return ReadBack();
    }

    public QuietHoursSchedule? ReadBack()
    {
        var text = store.ReadText(FileName, 512);
        if (text is null) { return null; }
        if (!text.StartsWith("1\n", StringComparison.Ordinal))
        { throw new InvalidDataException("The saved quiet-hours schema is unknown."); }
        try
        {
            var schedule = QuietHoursSchedule.Parse(text[2..]);
            if (!string.Equals(schedule.ToString(), text[2..], StringComparison.Ordinal))
            { throw new InvalidDataException("Saved quiet hours must use the canonical schedule encoding."); }
            return schedule;
        }
        catch (ArgumentException exception)
        { throw new InvalidDataException("The saved quiet-hours schedule or zone is invalid. No default was substituted.", exception); }
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(QuietHoursSchedule? schedule)
    {
        if (schedule is null) { store.Delete(FileName); }
        else { store.WriteText(FileName, "1\n" + schedule.ToString()); }
    }
}
