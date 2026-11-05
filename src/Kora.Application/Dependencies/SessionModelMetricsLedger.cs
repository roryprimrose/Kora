using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Dependencies;

/// <summary>Volatile, content-free accounting, independent of successful answer publication.</summary>
internal sealed class SessionModelMetricsLedger(TimeProvider time, ISessionModelMetricsStore? store = null)
{
    private readonly Lock gate = new();
    private readonly DateTimeOffset started = time.GetUtcNow();
    private readonly Dictionary<HostId<SessionIdentity>, Dictionary<ModelKey, ModelUsageSummary>> sessions = [];

    internal async ValueTask RecordAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
        ModelProviderReply? reply, TimeSpan elapsed)
    {
        var usage = reply?.ReportedSelection == provenance.Selection ? reply.TokenUsage : null;
        usage?.Validate();
        var selection = provenance.Selection;
        var key = new ModelKey(selection.Provider, profile?.Model.Value ?? selection.Model.Value.ToString("D"));
        var name = profile?.Model.Value ?? "Selected model";
        if (store is not null)
        {
            await store.RecordModelUsageAsync(new(provenance, name, usage, elapsed, time.GetUtcNow()),
                CancellationToken.None).ConfigureAwait(false);
            return;
        }
        lock (gate)
        {
            if (!sessions.TryGetValue(provenance.Request.SessionId, out var models))
            { sessions.Add(provenance.Request.SessionId, models = []); }
            Add(models, key, selection.Provider, name, usage, elapsed);
        }
    }

    private static void Add(Dictionary<ModelKey, ModelUsageSummary> models, ModelKey key, ModelProviderIdentity provider,
        string name, ModelTokenUsage? usage, TimeSpan elapsed)
    {
        models.TryGetValue(key, out var previous);
        models[key] = new(provider, name, checked((previous?.Executions ?? 0) + 1),
                checked((previous?.InputReportedExecutions ?? 0) + (usage?.InputTokens is null ? 0 : 1)),
                checked((previous?.OutputReportedExecutions ?? 0) + (usage?.OutputTokens is null ? 0 : 1)),
                checked((previous?.InputTokens ?? 0) + (usage?.InputTokens ?? 0)),
                checked((previous?.OutputTokens ?? 0) + (usage?.OutputTokens ?? 0)),
                usage?.ContextTokens, usage?.ContextCapacityTokens, (previous?.TotalElapsed ?? TimeSpan.Zero) + elapsed);
    }

    internal async Task<SessionModelMetrics> ReadAsync(HostId<SessionIdentity> session, CancellationToken token)
    {
        if (store is not null)
        {
            var records = await store.ReadModelUsageAsync(session, token).ConfigureAwait(false);
            if (records.IsDefault) { throw new InvalidDataException("Retained model usage is unavailable or malformed."); }
            var models = new Dictionary<ModelKey, ModelUsageSummary>();
            foreach (var record in records.OrderBy(record => record.ObservedAt))
            {
                token.ThrowIfCancellationRequested();
                record.Validate();
                if (record.Provenance.Request.SessionId != session)
                { throw new InvalidDataException("A usage observation belongs to another session."); }
                Add(models, new(record.Provenance.Selection.Provider, record.Model), record.Provenance.Selection.Provider,
                    record.Model, record.TokenUsage, record.Elapsed);
            }
            return Snapshot(models.Values, records.IsEmpty ? time.GetUtcNow() : records.Min(record => record.ObservedAt), retained: true);
        }
        lock (gate)
        {
            return Snapshot(sessions.TryGetValue(session, out var values) ? values.Values : [], started, retained: false);
        }
    }

    private SessionModelMetrics Snapshot(IEnumerable<ModelUsageSummary> source, DateTimeOffset first, bool retained)
    {
        ModelUsageSummary[] models = [.. source.OrderBy(model => model.Provider).ThenBy(model => model.Model, StringComparer.Ordinal)];
        return new(first, time.GetUtcNow(), models.Sum(model => model.Executions),
            models.Sum(model => model.InputReportedExecutions), models.Sum(model => model.OutputReportedExecutions),
            models.Sum(model => model.InputTokens), models.Sum(model => model.OutputTokens), [.. models], Retained: retained);
    }

    internal void Forget(HostId<SessionIdentity> session)
    {
        lock (gate) { sessions.Remove(session); }
    }

    private sealed record ModelKey(ModelProviderIdentity Provider, string Model);
}
