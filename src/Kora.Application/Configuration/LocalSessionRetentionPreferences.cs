using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalSessionRetentionPreferences : ISessionRetentionPreferences
{
    private const string FileName = "session-retention.txt";
    private const string PendingFileName = "session-retention-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalSessionRetentionPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalSessionRetentionPreferences(IPreferenceStore store) => this.store = store;

    public SessionRetentionSettings? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("A session-retention write is unconfirmed. Inspect saved state and audit receipts before explicit repair.");
        }
        return ReadBack();
    }

    public SessionRetentionSettings? ReadBack()
    {
        var lines = store.ReadLines(FileName);
        if (lines is null) { return null; }
        if (lines.Length != 3 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved session-retention format is unknown or malformed.");
        }
        try { return SessionRetentionSettings.Parse(lines[1], lines[2]); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved session retention is invalid.", exception);
        }
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(SessionRetentionSettings settings)
    {
        settings.Validate();
        store.WriteLines(FileName, ["1", settings.ArchiveDays.ToString(CultureInfo.InvariantCulture),
            settings.DeleteDays.ToString(CultureInfo.InvariantCulture)]);
    }
    public void Reset() => store.Delete(FileName);
}
