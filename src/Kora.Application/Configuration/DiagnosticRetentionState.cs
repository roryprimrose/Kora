using System.Text.Json;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record DiagnosticRetentionState(DiagnosticRetentionDays? Desired, DiagnosticRetentionDays? Effective,
    string Source, long Revision, string? Recovery)
{
    public int Schema => 1;
    public string Id => DiagnosticRetentionCommand.OptionId;
    public string Type => "integer-days";
    public int Minimum => DiagnosticRetentionDays.Minimum;
    public int Maximum => DiagnosticRetentionDays.Maximum;
    public int Default => DiagnosticRetentionDays.DefaultDays;
    public string Scope => "device-local; ordinary SQLite diagnostics only";
    public bool Available => Effective is not null;
    public bool ApplyNowAvailable => false;
    public string ApplicationTiming => "required audit, atomic save/readback and committed-intent receipt; newly committed rows only";
    public string Confirmation => "host-held proposal; original input; independent diagnostic session/generation; live owner/privacy/call and revisions";
    public string ResetEffect => "removes only SQLite diagnostic override; future commits use 30; existing deadlines unchanged";
    public string Excluded => "audit default 90 (30-365 domain); daily files 30 days/30 files; sessions/history; all grants and approvals; cleanup triggers";
    public string Syntax => DiagnosticRetentionCommand.Syntax;

    public static string Serialize(DiagnosticRetentionState state, long callRevision, string outcome)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { State = state, CallRevision = callRevision, Outcome = outcome }, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Diagnostic-retention result exceeds 64 KiB; no partial result is presented.");
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
