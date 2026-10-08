using System.Diagnostics;

using Kora.Definitions.Skills;
using Kora.Application.ViewModels;
using Kora.Application.Skills;
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
    private readonly SharedSkillSourcesWindowController sharedSources;
    private SkillPackagesWindow? window;

    internal SkillPackagesWindowController(MainViewModel viewModel, Func<bool> isCurrentHost,
        ILogger<SkillPackagesWindowController> logger, NativeDetailRenderer renderer,
        SharedSkillDiscoveryService discovery, ILogger<SharedSkillSourcesWindowController> sharedLogger)
    {
        this.viewModel = viewModel;
        this.isCurrentHost = isCurrentHost;
        this.logger = logger;
        this.renderer = renderer;
        sharedSources = new(viewModel, discovery, isCurrentHost, sharedLogger);
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
                window = new(catalogue, renderer, sharedSources.Open);
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
        sharedSources.Dispose();
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
}
