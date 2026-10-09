using System.Globalization;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalSessionQueuePreferences : ISessionQueuePreferences
{
    private const string FileName = "session-queue.txt";
    private const string PendingFileName = "session-queue-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalSessionQueuePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalSessionQueuePreferences(IPreferenceStore store) => this.store = store;

    public SessionQueuePreferences Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("A queue preference write is unconfirmed. Inspect saved state and audit/control receipts, explicitly repair, then refresh.");
        }
        return ReadBack();
    }

    public SessionQueuePreferences ReadBack()
    {
        var lines = store.ReadLines(FileName);
        if (lines is null) { return new(); }
        if (lines.Length != 3 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved queue preference schema or shape is unknown.");
        }
        try
        {
            return new SessionQueuePreferences().With(SessionQueueOption.PendingPerSession, Decode(lines[1]))
                .With(SessionQueueOption.ExecutionSlots, Decode(lines[2]));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved fixed-profile queue limits are malformed.", exception);
        }
    }

    private static string? Decode(string value) => string.Equals(value, "default", StringComparison.Ordinal) ? null : value;
    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(SessionQueuePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.IsDefault) { store.Delete(FileName); }
        else
        {
            store.WriteLines(FileName, ["1",
                preferences.PendingPerSession?.ToString(CultureInfo.InvariantCulture) ?? "default",
                preferences.ExecutionSlots?.ToString(CultureInfo.InvariantCulture) ?? "default"]);
        }
    }
}
