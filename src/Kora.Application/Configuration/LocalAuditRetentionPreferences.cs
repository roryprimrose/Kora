using System.Globalization;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalAuditRetentionPreferences : IAuditRetentionPreferences
{
    private const string FileName = "audit-retention.txt";
    private const string PendingFileName = "audit-retention-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalAuditRetentionPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalAuditRetentionPreferences(IPreferenceStore store) => this.store = store;

    public AuditRetentionDays? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("An audit-retention write is unconfirmed. Inspect saved state and required audit receipts before explicit repair; no automatic activation.");
        }
        return ReadBack();
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);

    public AuditRetentionDays? ReadBack()
    {
        var lines = store.ReadLines(FileName);
        if (lines is null) { return null; }
        if (lines.Length != 2 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved audit-retention format is unknown or malformed.");
        }
        try { return AuditRetentionDays.Parse(lines[1]); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved audit retention is invalid.", exception);
        }
    }

    public void Save(AuditRetentionDays days) =>
        store.WriteLines(FileName, ["1", new AuditRetentionDays(days.Days).Days.ToString(CultureInfo.InvariantCulture)]);

    public void Reset() => store.Delete(FileName);
}
