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
        if (store.ReadText(PendingFileName, 16) is not null)
        {
            throw new InvalidDataException("A queue preference write is unconfirmed. Inspect saved state and audit/control receipts, explicitly repair, then refresh.");
        }
        return ReadBack();
    }

    public SessionQueuePreferences ReadBack()
    {
        var text = store.ReadText(FileName, 256);
        if (text is null) { return new(); }
        using var reader = new StringReader(text);
        var values = new List<string>();
        while (reader.ReadLine() is { } line) { values.Add(line); }
        var lines = values.ToArray();
        var legacy = lines.Length == 3 && string.Equals(lines[0], "1", StringComparison.Ordinal);
        var lifetime = lines.Length == 4 && string.Equals(lines[0], "2", StringComparison.Ordinal);
        var active = lines.Length == 5 && string.Equals(lines[0], "3", StringComparison.Ordinal);
        if (!legacy && !lifetime && !active)
        {
            throw new InvalidDataException("The saved queue preference schema or shape is unknown.");
        }
        try
        {
            return new SessionQueuePreferences().With(SessionQueueOption.PendingPerSession, Decode(lines[1]))
                .With(SessionQueueOption.ExecutionSlots, Decode(lines[2]))
                .With(SessionQueueOption.PendingLifetimeMinutes, legacy ? null : Decode(lines[3]))
                .With(SessionQueueOption.ActiveBudgetMinutes, active ? Decode(lines[4]) : null);
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
            store.WriteLines(FileName, ["3",
                preferences.PendingPerSession?.ToString(CultureInfo.InvariantCulture) ?? "default",
                preferences.ExecutionSlots?.ToString(CultureInfo.InvariantCulture) ?? "default",
                preferences.PendingLifetimeMinutes?.ToString(CultureInfo.InvariantCulture) ?? "default",
                preferences.ActiveBudgetMinutes?.ToString(CultureInfo.InvariantCulture) ?? "default"]);
        }
    }
}
