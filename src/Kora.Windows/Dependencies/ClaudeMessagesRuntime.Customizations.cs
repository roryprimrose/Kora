using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeMessagesRuntime
{
    public Task<ClaudeTurnReply> RunSelectedAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, SelectedHostedDefinition selection,
        Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return RunCoreAsync(provenance, profile, context, selection, admit, current, token);
    }
}
