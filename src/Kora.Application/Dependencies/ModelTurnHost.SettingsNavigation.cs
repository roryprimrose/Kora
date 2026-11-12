using Kora.Core.Tools;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private Func<string, Func<bool>, CancellationToken, Task>? settingsNavigation;

    public void BindSettingsNavigation(Func<string, Func<bool>, CancellationToken, Task>? navigate) =>
        Volatile.Write(ref settingsNavigation, navigate);

    private async ValueTask<CapabilityReply> ShowSettingAsync(ModelTurn turn, string input, CancellationToken token)
    {
        var navigate = Volatile.Read(ref settingsNavigation);
        if (navigate is null)
        { return new(CapabilityOutcome.Unavailable, "native-settings-navigation-unavailable"); }
        bool Current() => !disposed && turn.IsRunning && host.IsCurrentHost && access.CanControl && access.CanInspect
            && turn.Provenance.Request.InvocationId is null
            && turn.Provenance.Request.Origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && access.ControlRevision == turn.ControlRevision && time.GetUtcNow() < turn.Context.ExpiresAt
            && ReferenceEquals(navigate, Volatile.Read(ref settingsNavigation)) && !token.IsCancellationRequested;
        if (!Current())
        { return new(CapabilityOutcome.Denied, "settings-navigation-source-not-admitted"); }
        try
        {
            return await Kora.Tools.Settings.SettingsView.ExecuteAsync(input, (query, cancellation) =>
                navigate(query, Current, cancellation), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            ToolDenied(logger);
            return new(CapabilityOutcome.Denied, "native-settings-navigation-retired");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ToolDenied(logger);
            return new(CapabilityOutcome.Unavailable, "native-settings-navigation-unavailable");
        }
    }
}
