using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Kora.Application.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.KnowledgeSources;

internal sealed partial class KnowledgeSourcesWindow
{
    private readonly KnowledgeSourceCaptureLimitsConfigurationService? captureLimits;
    private KnowledgeSourceCaptureLimitsReview? captureLimitsReview;
    private readonly TextBox limitFiles = new() { Text = "32" };
    private readonly TextBox limitPerFile = new() { Text = "262144" };
    private readonly TextBox limitCombined = new() { Text = "1048576" };

    private void BindCaptureLimits()
    {
        body.Children.Add(new TextBlock { Text = "Selected source's desired limits for its next separately reviewed refresh (files / bytes each / total bytes)." });
        AutomationProperties.SetName(limitFiles, "Source maximum file count");
        AutomationProperties.SetName(limitPerFile, "Source maximum per-file bytes");
        AutomationProperties.SetName(limitCombined, "Source maximum total bytes");
        body.Children.Add(limitFiles);
        body.Children.Add(limitPerFile);
        body.Children.Add(limitCombined);
        body.Children.Add(Button("Get selected source capture limits", () =>
        {
            var selected = Selected();
            return GetCaptureLimitsAsync(selected.Name, Eligible, selected);
        }));
        body.Children.Add(Button("Review selected source lower capture limits", () =>
        {
            if (!int.TryParse(limitFiles.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || !long.TryParse(limitPerFile.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var perFile)
                || !long.TryParse(limitCombined.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var combined))
            { throw new InvalidDataException("Enter three positive integer limits."); }
            var selected = Selected();
            return ReviewCaptureLimitsAsync(selected.Name, new(count, perFile, combined), Eligible, selected);
        }));
        body.Children.Add(Button("Review reset selected source capture limits", () =>
        {
            var selected = Selected();
            return ReviewCaptureLimitsAsync(selected.Name, null, Eligible, selected);
        }));
        limitFiles.PropertyChanged += OnCaptureLimitFieldChanged;
        limitPerFile.PropertyChanged += OnCaptureLimitFieldChanged;
        limitCombined.PropertyChanged += OnCaptureLimitFieldChanged;
    }

    private void OnCaptureLimitFieldChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty && captureLimitsReview is not null) { RetirePresentation(); }
    }

    internal Task OpenCaptureLimitsAsync(KnowledgeSourceCaptureLimitsCommand command, Func<bool> eligible)
    {
        var origin = HostActivity.RequireCurrent().Request.Origin;
        return RunAsync(async () =>
        {
            if (!eligible() || !Eligible()) { throw new InvalidOperationException("Source limits presentation admission changed."); }
            switch (command.Operation)
            {
                case "get": await GetCaptureLimitsAsync(command.Name, eligible); break;
                case "lower": await ReviewCaptureLimitsAsync(command.Name, command.Limits
                    ?? throw new InvalidDataException("Lower requires a complete limits tuple."), eligible); break;
                case "reset": await ReviewCaptureLimitsAsync(command.Name, null, eligible); break;
                case "confirm": await ConfirmCaptureLimitsAsync(eligible); break;
                case "cancel": CancelCaptureLimits(); break;
                default: throw new InvalidDataException("Unknown capture limits operation.");
            }
        }, origin);
    }

    private KnowledgeSourceCaptureLimitsConfigurationService LimitsService() => captureLimits
        ?? throw new InvalidOperationException("Source-specific capture limits configuration is unavailable.");

    private async Task GetCaptureLimitsAsync(string name, Func<bool> eligible, KnowledgeSource? expected = null)
    {
        RetirePresentation();
        var generation = epoch;
        var state = await LimitsService().GetAsync(name, () => Eligible() && eligible() && epoch == generation, lifetime.Token, expected);
        var desired = state.Saved ?? KnowledgeSourceCaptureLimits.HardMaximum;
        limitFiles.Text = desired.MaximumFiles.ToString(CultureInfo.InvariantCulture);
        limitPerFile.Text = desired.MaximumFileBytes.ToString(CultureInfo.InvariantCulture);
        limitCombined.Text = desired.MaximumCombinedBytes.ToString(CultureInfo.InvariantCulture);
        status.Text = $"Source '{state.Source.Name}': desired next refresh limits {Describe(desired)} "
            + $"({(state.Saved is null ? "hard defaults; no saved override" : "saved override")}). "
            + $"Current captured limits: {Describe(state.Source.Folder.CaptureLimits)}. No source read, refresh or preference write occurred.";
    }

    private async Task ReviewCaptureLimitsAsync(string name, KnowledgeSourceCaptureLimits? proposed, Func<bool> eligible,
        KnowledgeSource? expected = null)
    {
        RetirePresentation();
        var generation = epoch;
        var admission = host.CaptureKnowledgeSourceAnswerEligibility(HostActivity.RequireCurrent().Request.Origin);
        var exact = await LimitsService().ReviewAsync(name, proposed,
            () => Eligible() && eligible() && admission() && epoch == generation, lifetime.Token, expected);
        captureLimitsReview = exact;
        status.Text = $"CAPTURE LIMITS REVIEW for '{exact.Source.Name}'\n"
            + $"Registered root: {exact.Source.Folder.CanonicalPath}\n"
            + $"Current desired: {Describe(exact.Saved ?? KnowledgeSourceCaptureLimits.HardMaximum)}; "
            + $"proposed: {Describe(exact.Proposed ?? KnowledgeSourceCaptureLimits.HardMaximum)}"
            + (exact.Proposed is null ? " (reset; remove saved override)." : ".")
            + $"\nUnchanged hard maxima: {Describe(KnowledgeSourceCaptureLimits.HardMaximum)}. "
            + "Only this source's next separately reviewed explicit refresh uses this tuple. The entire excess source is rejected, never truncated. "
            + "No automatic refresh, scope change, pending/pinned snapshot change or model permission. "
            + "Numeric device-local preferences persist independently of sessions until reset/removal. Review expires in two minutes; confirm or cancel.";
        review.Children.Add(Button("Confirm this exact source capture limits preference", () => ConfirmCaptureLimitsAsync(Eligible)));
        review.Children.Add(Button("Cancel source capture limits review", () => { CancelCaptureLimits(); return Task.CompletedTask; }));
    }

    private async Task ConfirmCaptureLimitsAsync(Func<bool> eligible)
    {
        var exact = captureLimitsReview ?? throw new InvalidOperationException("No current exact source limits review.");
        captureLimitsReview = null;
        await LimitsService().ConfirmAsync(exact, () => Eligible() && eligible(), lifetime.Token);
        RetirePresentation();
        status.Text = "Exact source limits preference saved, audited and read back. No source was refreshed or captured. "
            + "Choose Refresh selected source for a separate complete read/save review.";
    }

    private void CancelCaptureLimits()
    {
        LimitsService().Cancel();
        RetirePresentation();
        status.Text = "Capture limits review cancelled. No preference or source changed.";
    }

    private static string Describe(KnowledgeSourceCaptureLimits limits) =>
        $"{limits.MaximumFiles} files / {limits.MaximumFileBytes} bytes each / {limits.MaximumCombinedBytes} bytes total";
}
