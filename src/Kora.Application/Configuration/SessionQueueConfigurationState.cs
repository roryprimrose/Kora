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
    public int Schema => 3;
    public string Scope => "device-local; fixed synchronous read-only application.get_version queue only";
    public string Timing => "after confirmed activation: pending lifetime captured at new enqueue; active budget captured once at future admission (including old pending entries); lowering capacity never evicts; existing pending metadata and admitted deadlines unchanged";
    public string Unavailable => "apply-now, current-task extension, automatic dispatch, general execution, provider/resource/effect qualification";
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
                new { Id = SessionQueueConfigurationCommand.LifetimeOptionId, Minimum = 1,
                    Maximum = SessionQueueLimits.MaximumPendingLifetimeMinutes, Default = SessionQueueLimits.DefaultPendingLifetimeMinutes,
                    Saved = state.Saved?.PendingLifetimeMinutes, Effective = state.Effective?.PendingLifetimeMinutes },
                new { Id = SessionQueueConfigurationCommand.ActiveOptionId, Minimum = 1,
                    Maximum = SessionQueueLimits.MaximumActiveBudgetMinutes, Default = SessionQueueLimits.DefaultActiveBudgetMinutes,
                    Saved = state.Saved?.ActiveBudgetMinutes, Effective = state.Effective?.ActiveBudgetMinutes },
            },
            PendingLifetimeUnit = "minutes",
            ActiveBudgetUnit = "minutes",
        }, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Queue configuration result exceeds 64 KiB; no partial result is presented.");
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
