using System.Globalization;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalDiagnosticRetentionPreferences : IDiagnosticRetentionPreferences
{
    private const string FileName = "sqlite-diagnostic-retention.txt";
    private const string PendingFileName = "sqlite-diagnostic-retention-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalDiagnosticRetentionPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalDiagnosticRetentionPreferences(IPreferenceStore store) => this.store = store;

    public DiagnosticRetentionDays? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("A SQLite diagnostic-retention write is unconfirmed. Inspect saved state and audit receipts before explicit repair and refresh.");
        }
        return ReadBack();
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);

    public DiagnosticRetentionDays? ReadBack()
    {
        var lines = store.ReadLines(FileName);
        if (lines is null) { return null; }
        if (lines.Length != 2 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved SQLite diagnostic-retention format is unknown or malformed.");
        }
        try { return DiagnosticRetentionDays.Parse(lines[1]); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved SQLite diagnostic retention is invalid.", exception);
        }
    }

    public void Save(DiagnosticRetentionDays days) =>
        store.WriteLines(FileName, ["1", new DiagnosticRetentionDays(days.Days).Days.ToString(CultureInfo.InvariantCulture)]);

    public void Reset() => store.Delete(FileName);
}
