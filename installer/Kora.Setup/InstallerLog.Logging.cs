using Microsoft.Extensions.Logging;

namespace Kora.Setup;

internal static partial class InstallerLog
{
    [LoggerMessage(1, LogLevel.Warning, "Installer preflight for {Component} reported {State}: {Detail}")]
    public static partial void Reported(ILogger logger, string component, InstallerDependencyState state, string detail);

    [LoggerMessage(2, LogLevel.Warning, "Installer preflight for {Component} failed")]
    public static partial void ComponentFailed(ILogger logger, string component, Exception exception);

    [LoggerMessage(3, LogLevel.Warning, "Installer dependency preflight failed")]
    public static partial void PreflightFailed(ILogger logger, Exception exception);

    [LoggerMessage(4, LogLevel.Warning, "Installer startup inspection for {Scope} reported {State}: {Detail}")]
    public static partial void StartupReported(ILogger logger, InstallScope scope, InstallerStartupState state, string detail);

    [LoggerMessage(5, LogLevel.Information, "Kora application launch requested after successful setup for {Scope}")]
    public static partial void ApplicationLaunchRequested(ILogger logger, InstallScope scope);

    [LoggerMessage(6, LogLevel.Error, "Kora remains installed but the approved completion launch failed")]
    public static partial void ApplicationLaunchFailed(ILogger logger, Exception exception);
}
