using System.ComponentModel;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Windows.Skills.RuntimeSetup;

namespace Kora;

internal sealed class BundledRuntimeSetupWindow : Window
{
    private readonly MainViewModel main;
    private readonly WindowsBundledRuntimeSetup setup;
    private readonly Func<bool> current;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox details = new() { IsReadOnly = true, IsUndoEnabled = false, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Button approve = new() { Content = "Approve exact registration operation", IsVisible = false };
    private readonly Button decline = new() { Content = "Decline", IsVisible = false };
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource<bool>? decision;
    private bool closeAfterJoin;
    private volatile bool retired;
    private int presentationAvailable;

    internal BundledRuntimeSetupWindow(MainViewModel main, WindowsBundledRuntimeSetup setup, Func<bool> current)
    {
        this.main = main;
        this.setup = setup;
        this.current = current;
        Title = "Bundled PowerShell runtime - preparation is not qualification";
        Width = 900; Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(status, "Bundled runtime preparation, reconciliation and containment status");
        AutomationProperties.SetName(details, "Exact complete installed file inventory and licence disclosure");
        var inspect = new Button { Content = "Inspect installed runtime", Command = Command(null) };
        actions.Children.Add(inspect);
        foreach (var operation in Enum.GetValues<BundledRuntimeSetupOperation>())
        {
            var button = new Button
            {
                Content = operation == BundledRuntimeSetupOperation.Forget ? "Forget registration only" : operation.ToString(),
                Command = Command(operation),
            };
            actions.Children.Add(button);
        }
        approve.Click += (_, _) => decision?.TrySetResult(true);
        decline.Click += (_, _) => decision?.TrySetResult(false);
        var cancel = new Button { Content = "Cancel and join owned operation" };
        cancel.Click += (_, _) => cancellation?.Cancel();
        Content = new StackPanel
        {
            Margin = new(18), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Existing protected installation only. No download, installation, runtime execution or effect approval.", TextWrapping = TextWrapping.Wrap },
                actions, status,
                new ScrollViewer { Content = details, MaxHeight = 420, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { approve, decline, cancel } },
            },
        };
        status.Text = setup.Description;
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty) { Volatile.Write(ref presentationAvailable, IsVisible ? 1 : 0); }
        };
        main.PrivacyClosureRequested += PrivacyClosed;
        main.PropertyChanged += MainChanged;
        Closing += (_, args) =>
        {
            Retire();
            if (cancellation is not null) { closeAfterJoin = true; args.Cancel = true; Hide(); }
        };
        Closed += (_, _) =>
        {
            Retire();
            main.PrivacyClosureRequested -= PrivacyClosed;
            main.PropertyChanged -= MainChanged;
        };
    }

    internal async Task RunAsync(BundledRuntimeSetupOperation? operation)
    {
        if (cancellation is not null || retired) { return; }
        using var lifetime = new CancellationTokenSource();
        cancellation = lifetime;
        actions.IsEnabled = false;
        var revision = main.CallPolicyRevision;
        bool Current() => !retired && Volatile.Read(ref presentationAvailable) == 1 && current() && main.CanRevealPrivatePresentation
            && main.IsSkillReviewCallEligible && revision == main.CallPolicyRevision && !lifetime.IsCancellationRequested;
        try
        {
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                if (!Current()) { throw new UnauthorizedAccessException("Current original-user runtime review is unavailable."); }
                if (operation is null)
                {
                    var state = await setup.InspectAsync(lifetime.Token);
                    if (!Current()) { throw new UnauthorizedAccessException("Runtime inspection retired before private publication."); }
                    status.Text = state.Description;
                    details.Text = DescribeInventory(state);
                    return;
                }
                var plan = await setup.ReviewAsync(operation.Value, Current, lifetime.Token);
                if (!Current()) { throw new UnauthorizedAccessException("Runtime review retired before private publication."); }
                details.Text = plan.Disclosure + "\n\n" + DescribeInventory(plan.Expected);
                var pendingDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                decision = pendingDecision;
                approve.IsVisible = decline.IsVisible = true;
                using var cancel = lifetime.Token.Register(() => pendingDecision.TrySetCanceled(lifetime.Token));
                var consent = await pendingDecision.Task;
                approve.IsVisible = decline.IsVisible = false;
                var result = await setup.ApplyAsync(plan, consent, lifetime.Token);
                if (!Current()) { throw new UnauthorizedAccessException("Runtime result retired before private publication."); }
                status.Text = result.Description;
                details.Text = DescribeInventory(result);
            });
        }
        catch (OperationCanceledException)
        {
            if (!retired) { status.Text = "Cancelled and all owned I/O joined. Registration may be unconfirmed. Inspect and explicitly verify before retrying; no runtime was executed."; }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException or PlatformNotSupportedException
            or System.Security.Cryptography.CryptographicException)
        {
            if (!retired)
            {
                status.Text = "Runtime operation not confirmed. " + exception.Message
                    + " Inspect and review recovery. Invalid saved metadata must be restored or explicitly removed from the local registration preference; no fallback runtime is selected.";
                details.Text = string.Empty;
                main.ReportHostInteractionFailure(status.Text);
            }
        }
        finally
        {
            decision = null;
            approve.IsVisible = decline.IsVisible = false;
            cancellation = null;
            actions.IsEnabled = !retired;
            if (closeAfterJoin) { Close(); }
        }
    }

    private static string DescribeInventory(BundledRuntimeInspection state) => state.Installation is { } installed
        ? $"Host-selected executable: {installed.Executable}\nVersion: {installed.Version}\n"
            + $"{installed.Files.Count} files, {installed.Bytes:N0} existing bytes; no acquisition or staging.\n"
            + "No executable search/PATH fallback. Installed files are not permission to load ambient modules or children.\n"
            + string.Join('\n', installed.Files.Select(file => $"{file.Name} | {file.Bytes} bytes | SHA-256 {file.Digest}"))
        : state.InstallationInspected
            ? "No supported independently protected Program Files PowerShell installation was observed. No executable was searched elsewhere or started."
            : "Only the Kora registration was inspected/forgotten. Runtime files were not inspected, modified or removed.";

    private void PrivacyClosed(object? sender, EventArgs args) => Close();
    private void MainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(MainViewModel.CallPolicyRevision), StringComparison.Ordinal)) { Close(); }
    }

    private void Retire()
    {
        retired = true;
        Volatile.Write(ref presentationAvailable, 0);
        cancellation?.Cancel();
        decision?.TrySetResult(false);
        status.Text = details.Text = string.Empty;
        approve.IsVisible = decline.IsVisible = false;
        actions.IsEnabled = false;
    }

    private AsyncCommand Command(BundledRuntimeSetupOperation? operation) => new(() => RunAsync(operation),
        exception => main.ReportHostInteractionFailure("Bundled runtime operation failed: " + exception.GetType().Name));
}
