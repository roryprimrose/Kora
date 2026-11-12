using Kora.Core.Tools;

namespace Kora.Tools.Settings;

public static class SettingsView
{
    public static async ValueTask<CapabilityReply> ExecuteAsync(string input,
        Func<string, CancellationToken, Task> show, CancellationToken token)
    {
        var parsed = CapabilityInputPolicy.Read(SettingsNavigationTool.Descriptor, input);
        if (parsed.DenialReason is { } denial) { return new(CapabilityOutcome.Denied, denial); }
        token.ThrowIfCancellationRequested();
        await show(parsed.SettingsQuery!, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new(CapabilityOutcome.Succeeded, SettingsNavigationTool.ResultReason,
            Descriptor: SettingsNavigationTool.Descriptor);
    }
}
