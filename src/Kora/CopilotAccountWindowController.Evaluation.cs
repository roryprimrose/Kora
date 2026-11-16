using Avalonia.Controls;
using Kora.Application.Infrastructure;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;

namespace Kora;

internal sealed partial class CopilotAccountWindowController
{
    private StackPanel CreateEvaluationControls(Func<bool> live, Func<bool> remainsEligible,
        Action enabled, CancellationToken token)
    {
        var path = new TextBox { PlaceholderText = @"Installed Copilot runtime executable, e.g. C:\...\copilot.exe", MaxLength = 4096 };
        var status = new TextBlock
        {
            Text = "Optional live evaluation, not production qualification. Install the Copilot runtime separately from a trusted source. "
                + "The pinned SDK declares runtime 1.0.103; other runtime versions may be incompatible. Kora does not install or download it.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var consent = new CheckBox
        {
            Content = "I approve executing the exact reviewed runtime for live evaluation and accept its account terms, entitlement and possible usage charges. "
                + "Runtime lifecycle/network/storage qualification is incomplete; Kora does not enforce a monetary cap.",
            IsChecked = false,
        };
        CopilotEvaluationCandidate? reviewed = null;
        string? disclosure = null;
        var approve = new Button { Content = "Enable this reviewed runtime for live evaluation", IsEnabled = false };
        var inspect = new Button { Content = "Inspect installed runtime (no execution)" };
        void Failed(Exception exception)
        {
            SetupFailed(logger, exception.GetType().Name);
            if (live()) { status.Text = disclosure + Environment.NewLine + "Runtime evaluation unavailable: " + exception.Message; }
        }
        path.TextChanged += (_, _) =>
        {
            reviewed = null;
            disclosure = null;
            consent.IsChecked = false;
            approve.IsEnabled = false;
            evaluation!.DiscardPendingReview();
        };
        inspect.Command = new AsyncCommand(async () =>
        {
            if (!live()) { throw new InvalidOperationException("The original runtime controls retired."); }
            var selected = path.Text ?? string.Empty;
            reviewed = null;
            consent.IsChecked = false;
            approve.IsEnabled = inspect.IsEnabled = false;
            accounts.Revoke();
            Interlocked.Increment(ref operations);
            try
            {
                var observing = admission.RunObservedAsync(RequestOrigin.LocalUi, live,
                    ct => evaluation!.ObserveAsync(selected, live, ct), (_, _, result) => result, token);
                work = observing;
                var observed = await observing;
                if (live() && string.Equals(path.Text, selected, StringComparison.Ordinal))
                {
                    reviewed = observed;
                    disclosure = "Review before executing: " + observed.ExecutablePath + Environment.NewLine
                        + "SHA-256: " + observed.Sha256 + Environment.NewLine
                        + "This identifies the selected bytes, not trusted provenance or production qualification. "
                        + "Approval lasts one hour, in memory only, and is revoked by privacy loss or Forget. "
                        + "Runtime working/state/temp paths are private and separate from your workspace; incidental runtime files may remain there.";
                    status.Text = disclosure;
                    approve.IsEnabled = true;
                }
                else { evaluation!.Revoke(); }
            }
            finally
            {
                Interlocked.Decrement(ref operations);
                if (live()) { inspect.IsEnabled = true; }
            }
        }, Failed);
        approve.Command = new AsyncCommand(async () =>
        {
            if (!live()) { throw new InvalidOperationException("The original runtime controls retired."); }
            if (reviewed is null || consent.IsChecked != true)
            {
                status.Text = disclosure + Environment.NewLine
                    + "Inspect the runtime and explicitly approve the reviewed bytes, terms and charges first. Nothing was enabled.";
                return;
            }
            var current = reviewed;
            Interlocked.Increment(ref operations);
            try
            {
                work = admission.RunObservedAsync(RequestOrigin.LocalUi, live, ct =>
                {
                    evaluation!.Admit(current, live, remainsEligible, ct);
                    return Task.FromResult(true);
                }, (_, _, result) => result, token);
#pragma warning disable VSTHRD003 // Join this controller's actual owned native approval operation.
                await work;
#pragma warning restore VSTHRD003
                if (live()) { enabled(); approve.IsEnabled = false; }
            }
            finally { Interlocked.Decrement(ref operations); }
        }, Failed);
        return new StackPanel { Spacing = 8, Children = { status, path, inspect, consent, approve } };
    }
}
