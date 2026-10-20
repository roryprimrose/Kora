namespace Kora.Application.Presentation;

/// <summary>The native singleton Settings navigation boundary.</summary>
public interface ISettingsSectionNavigation
{
    /// <summary>Captures the current presenter lifetime before UI dispatch.</summary>
    /// <returns>A guard that never revives after native lifetime loss.</returns>
    Func<bool> CaptureLifetime();

    /// <summary>Shows and activates the existing Settings surface at an exact destination.</summary>
    /// <param name="section">The closed typed destination.</param>
    /// <param name="admitted">The original request admission guard.</param>
    void Navigate(SettingsSection section, Func<bool> admitted);
}
