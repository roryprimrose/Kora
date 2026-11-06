using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Configuration;

namespace Kora;

internal sealed class ManualStartupPresentationController(StartupPresentationConfigurationService configuration,
    MainViewModel main, Action<Func<bool>> showWorkspace)
{
    internal const string ManualLaunchArgument = "--manual-startup";
    private bool applied;

    internal static bool IsManualLaunch(IReadOnlyList<string> args) =>
        args.Count == 1 && string.Equals(args[0], ManualLaunchArgument, StringComparison.Ordinal);

    internal async Task ApplyAsync(bool manualLaunch, CancellationToken token)
    {
        if (applied) { throw new InvalidOperationException("Manual startup presentation has already been consumed; no deferred replay is permitted."); }
        applied = true;
        if (!manualLaunch) { return; }
        var host = main.CaptureStartupPresentationEligibility();
        var configurationCurrent = configuration.CaptureStartupCurrent();
        bool Eligible() => host() && configurationCurrent();
        if (!Eligible()) { throw new InvalidOperationException("Manual startup presentation is unavailable on the current host; no deferred replay is permitted."); }
        var saved = await configuration.ReadStartupAsync(token);
        token.ThrowIfCancellationRequested();
        if (!Eligible()) { throw new InvalidOperationException("Manual startup presentation held: ownership, privacy, call, input, configuration or pending decision changed. Use explicit existing navigation; no unlock/call-end replay."); }
        if (saved != await configuration.ReadStartupAsync(token))
        { throw new InvalidDataException("Manual startup intent changed before native publication; review it again."); }
        token.ThrowIfCancellationRequested();
        if (!Eligible()) { throw new InvalidOperationException("Manual startup presentation admission changed before native publication."); }
        if (saved == StartupPresentation.Workspace) { showWorkspace(Eligible); }
    }
}
