using System.Runtime.CompilerServices;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly ConditionalWeakTable<ModelTurn, SettingInspectionReview> settingDiscoveries = new();

    internal void BindSettingsDiscovery(ModelTurn turn, SettingInspectionReview review)
    {
        if (turn.Owner != owner || turn.Context.ToolMode != ModelToolMode.SettingsDiscovery
            || review.ToolMode != ModelToolMode.SettingsDiscovery
            || turn.Provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || turn.Provenance.Request.SessionId != review.Destination.SessionId
            || turn.Provenance.Request.Origin != review.Original.Origin
            || !string.Equals(turn.Context.UserRequest, review.Question, StringComparison.Ordinal)
            || !string.Equals(turn.Context.SystemPolicy, review.ModelPolicy, StringComparison.Ordinal)
            || turn.Context.Evidence.Length != 1
            || !string.Equals(turn.Context.Evidence[0].Content, review.Source, StringComparison.Ordinal)
            || turn.Context.Evidence[0].Disclosure != ModelEvidenceDisclosure.LocalOnly
            || time.GetUtcNow() >= review.ExpiresAt || !settingDiscoveries.TryAdd(turn, review))
        { throw new InvalidOperationException("Settings discovery requires the exact original reviewed local value source."); }
    }

    private bool SettingsDiscoveryCurrent(ModelTurn turn) =>
        turn.Context.ToolMode != ModelToolMode.SettingsDiscovery
        || settingDiscoveries.TryGetValue(turn, out var review) && time.GetUtcNow() < review.ExpiresAt;
}
