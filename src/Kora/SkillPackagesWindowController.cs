using System.Diagnostics;
using System.ComponentModel;

using Kora.Definitions.Skills;
using Kora.Application.ViewModels;
using Kora.Application.Skills;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SkillPackagesWindowController : IDisposable, ISkillReviewNavigation
{
    private static readonly ActivitySource ActivitySource =
        new("Kora.Desktop", typeof(SkillPackagesWindowController).Assembly.GetName().Version!.ToString());
    private readonly MainViewModel viewModel;
    private readonly Func<bool> isCurrentHost;
    private readonly ILogger<SkillPackagesWindowController> logger;
    private readonly NativeDetailRenderer renderer;
    private readonly SharedSkillSourcesWindowController sharedSources;
    private readonly Func<SkillPackageCatalogue> loadCatalogue;
    private SkillPackagesWindow? window;
    private bool disposed;
    private bool opening;
    private bool closeAfterOpening;
    private Action<SkillPackageSnapshot>? executeBundled;
    private Action<SkillPackageSnapshot>? executeBoundBundled;
    private bool controlSessionAvailable;
    private SkillPackageSnapshot? reviewedPackage;

    internal void BindBundledExecution(Action<SkillPackageSnapshot>? execute, Action<SkillPackageSnapshot>? executeBound = null)
    { executeBundled = execute; executeBoundBundled = executeBound; }

    internal void SetControlSessionAvailable(bool available)
    { controlSessionAvailable = available; window?.SetControlSessionAvailable(available); }

    internal bool OpenPackage(string id, Func<bool> eligible) =>
        TryOpen(SkillReviewSurface.BundledPackages, eligible) && window is not null && window.SelectPackage(id);

    internal bool IsReviewing(SkillPackageSnapshot package) => Volatile.Read(ref reviewedPackage) is { } selected
        && string.Equals(selected.DeclaredResourceDigest, package.DeclaredResourceDigest, StringComparison.Ordinal);

    internal SkillPackagesWindowController(MainViewModel viewModel, Func<bool> isCurrentHost,
        ILogger<SkillPackagesWindowController> logger, NativeDetailRenderer renderer,
        SharedSkillDiscoveryService discovery, ILogger<SharedSkillSourcesWindowController> sharedLogger,
        Func<SkillPackageCatalogue>? loadCatalogue = null)
    {
        this.viewModel = viewModel;
        this.isCurrentHost = isCurrentHost;
        this.logger = logger;
        this.renderer = renderer;
        this.loadCatalogue = loadCatalogue ?? EmbeddedSkillCatalogue.Load;
        sharedSources = new(viewModel, discovery, isCurrentHost, sharedLogger);
        viewModel.PrivacyClosureRequested += OnPrivacyClosureRequested;
        viewModel.PropertyChanged += OnMainChanged;
    }

    public bool TryOpen(SkillReviewSurface surface, Func<bool> eligible)
    {
        if (disposed || opening || !eligible()) { return false; }
        try
        {
            return surface switch
            {
                SkillReviewSurface.BundledPackages => OpenPackages(eligible),
                SkillReviewSurface.SharedSources => sharedSources.TryOpen(eligible),
                _ => false,
            };
        }
        catch (InvalidOperationException)
        {
            InspectionDenied(logger, "native-review-unavailable");
            viewModel.ReportHostInteractionFailure("The requested native skill review is unavailable. No inspector was confirmed opened.");
            return false;
        }
    }

    internal void Open() => TryOpen(SkillReviewSurface.BundledPackages, static () => true);

    private bool OpenPackages(Func<bool> eligible)
    {
        using var activity = ActivitySource.StartActivity("presentation.skill-review");
        activity?.SetStatus(ActivityStatusCode.Error);
        if (disposed || !isCurrentHost() || !viewModel.CanRevealPrivatePresentation || !viewModel.IsSkillReviewCallEligible)
        {
            InspectionDenied(logger, "current-host-presentation-required");
            viewModel.ReportHostInteractionFailure("Skill package review requires current host ownership, permitted local presentation and a known call policy.");
            return false;
        }
        try
        {
            if (window is null)
            {
                var catalogue = loadCatalogue();
                if (!eligible() || disposed || !isCurrentHost() || !viewModel.CanRevealPrivatePresentation
                    || !viewModel.IsSkillReviewCallEligible)
                { return false; }
                var revision = viewModel.CallPolicyRevision;
                window = new(catalogue, renderer, sharedSources.Open,
                    () => !disposed && isCurrentHost() && viewModel.CanRevealPrivatePresentation && viewModel.CallPolicyRevision == revision,
                    package => executeBundled?.Invoke(package), package => executeBoundBundled?.Invoke(package));
                window.SetControlSessionAvailable(controlSessionAvailable);
                window.Closed += OnClosed;
                window.PackageChanged += package => Volatile.Write(ref reviewedPackage, package);
                Volatile.Write(ref reviewedPackage, window.SelectedPackage);
                InspectionAdmitted(logger, catalogue.Packages.Count, false);
            }
            var opened = window;
            if (!opened.IsVisible)
            {
                opening = true;
                try { opened.Show(); }
                catch (InvalidOperationException) { Close(); throw; }
                finally
                {
                    opening = false;
                    if (closeAfterOpening) { closeAfterOpening = false; Close(); }
                }
            }
            if (!ReferenceEquals(window, opened) || disposed || !eligible()
                || !isCurrentHost() || !viewModel.CanRevealPrivatePresentation || !viewModel.IsSkillReviewCallEligible)
            {
                opened.Close();
                return false;
            }
            opened.Activate();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return true;
        }
        catch (InvalidDataException exception)
        {
            InspectionFailed(logger, exception, "invalid-embedded-catalogue");
            viewModel.ReportHostInteractionFailure("Embedded skill resources failed exact verification. No package is admitted. Repair the application through its verified distribution channel.");
            return false;
        }
    }

    public void Dispose()
    {
        disposed = true;
        viewModel.PrivacyClosureRequested -= OnPrivacyClosureRequested;
        viewModel.PropertyChanged -= OnMainChanged;
        sharedSources.Dispose();
        Close();
    }

    private void OnPrivacyClosureRequested(object? sender, EventArgs eventArgs) => Close();

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(MainViewModel.CallPolicyRevision), StringComparison.Ordinal)) { Close(); }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Volatile.Write(ref reviewedPackage, null);
        window = null;
    }

    private void Close()
    {
        Volatile.Write(ref reviewedPackage, null);
        // WindowOpened is raised before native Show initialization has finished.
        if (opening)
        {
            closeAfterOpening = true;
            window?.ClearPrivateContent();
            return;
        }
        if (window is not null)
        {
            window.Closed -= OnClosed;
            window.Close();
            window = null;
        }
    }
}