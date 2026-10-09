using System.Text.Json;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record AuditRetentionState(AuditRetentionDays? Saved, AuditRetentionDays? Effective,
    string Source, long Revision, string? Recovery)
{
    public AuditRetentionDays Desired => Saved ?? AuditRetentionDays.Default;
    public int Schema => 1;
    public string Id => AuditRetentionCommand.OptionId;
    public string Type => "integer-days";
    public int Minimum => AuditRetentionDays.Minimum;
    public int Maximum => AuditRetentionDays.Maximum;
    public int Default => AuditRetentionDays.DefaultDays;
    public string Scope => "device-local; new committed required authority audit and independently qualified diagnostic audit projections";
    public bool Available => Effective is not null;
    public bool ApplyNowAvailable => false;
    public string ApplicationTiming => "requested and terminal audit plus committed-intent receipt use prior policy; atomic save/readback and confirmation precede activation; newly committed rows only";
    public string Confirmation => "host-held proposal; original input; independent audit session/generation; live owner/privacy/call and revisions";
    public string ResetEffect => "removes only audit override; future audit commits use 90; existing deadlines unchanged";
    public string Excluded => "ordinary SQLite diagnostics (independent 1-365/default 30); daily files 30 days/30 files; session/history; all grant records, validity and scopes; approvals/tasks/questions; deletion/pruning and cleanup triggers";
    public string Syntax => AuditRetentionCommand.Syntax;

    public static string Serialize(AuditRetentionState state, long callRevision, string outcome)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { State = state, CallRevision = callRevision, Outcome = outcome }, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Audit-retention result exceeds 64 KiB; no partial result is presented.");
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
