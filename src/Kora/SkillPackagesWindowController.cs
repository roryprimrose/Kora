using System.Diagnostics;

using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SkillPackagesWindowController : IDisposable
{
    private static readonly ActivitySource ActivitySource =
        new("Kora.Desktop", typeof(SkillPackagesWindowController).Assembly.GetName().Version!.ToString());
    private readonly MainViewModel viewModel;
    private readonly Func<bool> isCurrentHost;
    private readonly ILogger<SkillPackagesWindowController> logger;
    private readonly NativeDetailRenderer renderer;
    private SkillPackagesWindow? window;

    internal SkillPackagesWindowController(MainViewModel viewModel, Func<bool> isCurrentHost,
        ILogger<SkillPackagesWindowController> logger, NativeDetailRenderer renderer)
    {
        this.viewModel = viewModel;
        this.isCurrentHost = isCurrentHost;
        this.logger = logger;
        this.renderer = renderer;
        viewModel.PrivacyClosureRequested += OnPrivacyClosureRequested;
    }

    internal void Open()
    {
        using var activity = ActivitySource.StartActivity("presentation.skill-review");
        activity?.SetStatus(ActivityStatusCode.Error);
        if (!isCurrentHost() || !viewModel.CanRevealPrivatePresentation)
        {
            InspectionDenied(logger, "current-host-presentation-required");
            viewModel.ReportHostInteractionFailure("Skill package review is unavailable until current host ownership and local presentation are permitted.");
            return;
        }
        try
        {
            if (window is null)
            {
                var catalogue = EmbeddedSkillCatalogue.Load();
                window = new(catalogue, renderer);
                window.Closed += OnClosed;
                InspectionAdmitted(logger, catalogue.Packages.Count, false);
            }
            if (!window.IsVisible)
            {
                window.Show();
            }
            window.Activate();
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (InvalidDataException exception)
        {
            InspectionFailed(logger, exception, "invalid-embedded-catalogue");
            viewModel.ReportHostInteractionFailure("Embedded skill resources failed exact verification. No package is admitted. Repair the application through its verified distribution channel.");
        }
    }

    public void Dispose()
    {
        viewModel.PrivacyClosureRequested -= OnPrivacyClosureRequested;
        Close();
    }

    private void OnPrivacyClosureRequested(object? sender, EventArgs eventArgs) => Close();

    private void OnClosed(object? sender, EventArgs eventArgs) => window = null;

    private void Close()
    {
        if (window is not null)
        {
            window.Closed -= OnClosed;
            window.Close();
            window = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Embedded skill inspection denied: {ReasonCode}")]
    private static partial void InspectionDenied(ILogger logger, string reasonCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Embedded skill inspection admitted: {PackageCount} packages, {InvocationAvailable}")]
    private static partial void InspectionAdmitted(ILogger logger, int packageCount, bool invocationAvailable);

    [LoggerMessage(Level = LogLevel.Error, Message = "Embedded skill inspection failed: {ReasonCode}")]
    private static partial void InspectionFailed(ILogger logger, Exception exception, string reasonCode);
}
