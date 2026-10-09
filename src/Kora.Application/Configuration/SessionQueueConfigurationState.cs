using System.Text;
using System.Text.Json;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed record SessionQueueConfigurationState(SessionQueuePreferences? Saved, SessionQueueLimits? Effective,
    long Revision, string? Recovery)
{
    public bool Available => Effective is not null;
    public string Source => !Available ? "unavailable" : Saved!.IsDefault ? "default" : "saved";
    public int Schema => 1;
    public string Scope => "device-local; fixed synchronous read-only application.get_version queue only";
    public string Timing => "future admission only; lowering capacity never evicts; existing pending and active deadlines unchanged";
    public string Unavailable => "pending lifetime (fixed 30 minutes), active budget (fixed 5 minutes), automatic dispatch, general execution, provider/resource/effect qualification";
    public string Syntax => SessionQueueConfigurationCommand.Syntax;

    public static string Serialize(SessionQueueConfigurationState state, long callRevision, string outcome)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            State = state, CallRevision = callRevision, Outcome = outcome,
            Options = new[]
            {
                new { Id = SessionQueueConfigurationCommand.PendingOptionId, Minimum = 1,
                    Maximum = SessionQueueLimits.MaximumPendingPerSession, Default = SessionQueueLimits.MaximumPendingPerSession,
                    Saved = state.Saved?.PendingPerSession, Effective = state.Effective?.PendingPerSession },
                new { Id = SessionQueueConfigurationCommand.SlotsOptionId, Minimum = 1,
                    Maximum = SessionQueueLimits.MaximumExecutionSlots, Default = SessionQueueLimits.DefaultExecutionSlots,
                    Saved = state.Saved?.ExecutionSlots, Effective = state.Effective?.ExecutionSlots },
            },
        }, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Queue configuration result exceeds 64 KiB; no partial result is presented.");
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
