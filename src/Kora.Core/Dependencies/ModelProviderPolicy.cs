using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

/// <summary>Immutable volatile session policy; persistence and native settings are separate integrations.</summary>
public sealed record ModelProviderPolicy(
    HostId<SessionIdentity> Session, HostRevision Revision, ModelProviderMode Mode,
    ModelProviderSelection Local, ModelProviderSelection Hosted)
{
    public bool IsValid => Session.Value != Guid.Empty && Revision.Value > 0
        && ModelProviderModePreference.IsValid(Mode)
        && ValidSelection(Local, ModelProviderIdentity.Ollama) && ValidSelection(Hosted, ModelProviderIdentity.Copilot);

    public ModelProviderSelection? Select(ModelTurnChoice choice)
    {
        if (!IsValid || !Enum.IsDefined(choice)
            || Mode == ModelProviderMode.LocalOnly && choice == ModelTurnChoice.Hosted)
        {
            return null;
        }
        return choice switch
        {
            ModelTurnChoice.Local => Local,
            ModelTurnChoice.Hosted => Hosted,
            _ => Mode == ModelProviderMode.HostedPreferred ? Hosted : Local,
        };
    }

    private static bool ValidSelection(ModelProviderSelection? selection, ModelProviderIdentity provider) =>
        selection is not null && selection.Provider == provider && selection.IsValid;
}
