using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

/// <summary>Owns native scope reads through actual completion, including cancellation-ignoring stores.</summary>
internal sealed class SessionInstructionScopeController(Func<Func<bool>> captureHost, Func<bool> canReveal,
    Action<string> reportFailure, SessionInstructionScopeCommandService commands,
    SessionInstructionScopeBroker broker, Func<SessionsViewModel?> model, Func<Window?> owner)
{
    private SessionInstructionScopeView? window;
    private SessionInstructionScopeWindow? adapter;
    private SessionWorkspaceEntry? windowTarget;
    private Func<bool>? windowTargetEligible;
    private CancellationTokenSource? cancellation;
    private long epoch;
    private bool disposed;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal Task CloseOperation { get; private set; } = Task.CompletedTask;
    internal SessionInstructionScopeWindow? Window => adapter;
    internal SessionInstructionScopeView? View => window;

    internal void Open()
    {
        if (disposed || !canReveal()) { throw new InvalidOperationException("Private session scope presentation is unavailable."); }
        if (window is not null) { _ = RequireWindowTarget(); }
        if (window is null)
        {
            var target = Target();
            var parent = owner();
            SessionInstructionScopeView opened;
            if (parent is ResponseWindow primary)
            { opened = new(primary, () => !disposed && canReveal()); }
            else
            {
                adapter = new();
                opened = adapter.View;
            }
            window = opened;
            windowTarget = target;
            windowTargetEligible = model()!.CaptureDraftPresentationEligibility();
            opened.Sources.SelectionChanged += (_, _) =>
            {
                if (!ReferenceEquals(window, opened)) { return; }
                commands.Retire(); opened.Confirm.IsEnabled = false;
                opened.Preview.Text = "Selection changed. Review the complete source before confirming."; epoch++;
            };
            opened.Refresh.Click += (_, _) =>
            {
                try { RequireNativeView(opened); Refresh(opened); }
                catch (InvalidOperationException exception)
                {
                    opened.Preview.Text = "Unconfirmed: " + exception.Message;
                    reportFailure(exception.Message);
                }
            };
            opened.Review.Click += async (_, _) => await RunNativeAsync(opened, new(SessionCommandOperation.InstructionScopeReview),
                opened.Sources.SelectedItem as ArtifactDefinition);
            opened.Confirm.Click += async (_, _) => await RunNativeAsync(opened, new(SessionCommandOperation.InstructionScopeConfirm));
            opened.Stop.Click += async (_, _) => await RunNativeAsync(opened, new(SessionCommandOperation.InstructionScopeStop));
            opened.Status.Click += async (_, _) => await RunNativeAsync(opened, new(SessionCommandOperation.InstructionScopeStatus));
            opened.PresentationLost += (_, _) =>
            {
                RetirePresentation(opened);
            };
            opened.Closed += async (_, _) =>
            {
                if (ReferenceEquals(window, opened))
                {
                    window = null; adapter = null; windowTarget = null; windowTargetEligible = null;
                    RevokeReview();
                    await (CloseOperation = JoinAsync());
                }
                opened.Preview.Text = string.Empty;
                opened.Sources.ItemsSource = null;
            };
            Refresh(opened);
            if (adapter is { } legacy)
            { if (parent is null) { legacy.Show(); } else { legacy.Show(parent); } }
        }
        var view = window;
        if (!view.IsNativePresented && view.Owner is ResponseWindow workbench)
        {
            workbench.ShowSessions(expanded: true, preservePresentation: true);
            workbench.ShowNativeReview(view, "Instruction for this Active session", view,
                () => !disposed && ReferenceEquals(window, view) && canReveal(),
                shown => { if (!shown) { RetirePresentation(view); } });
            view.RefreshPresentation();
        }
        view.Owner.Activate();
    }

    private void RetirePresentation(SessionInstructionScopeView opened)
    {
        if (!ReferenceEquals(window, opened)) { return; }
        RevokeReview();
        if (opened.Owner is not ResponseWindow) { return; }
        window = null;
        windowTarget = null;
        windowTargetEligible = null;
        CloseOperation = CloseRetiredAsync(opened);
    }

    private async Task CloseRetiredAsync(SessionInstructionScopeView opened)
    {
        await JoinAsync();
        await Dispatcher.UIThread.InvokeAsync(opened.Dispose, DispatcherPriority.Background);
    }

    private SessionWorkspaceEntry Target() => model()?.SelectedSessionRecord is { Authority.IsActive: true } target
        ? target : throw new InvalidOperationException("Select the exact Active session in Sessions first.");

    private SessionWorkspaceEntry RequireWindowTarget()
    {
        if (windowTarget is not { Authority.IsActive: true } target
            || model()?.SelectedSessionRecord != target || windowTargetEligible?.Invoke() != true)
        {
            RevokeReview();
            throw new InvalidOperationException("The exact Active session selection changed. Close this scope view, "
                + "select the intended Active session and reopen its instruction controls.");
        }
        return target;
    }

    private void Refresh(SessionInstructionScopeView opened)
    {
        var target = RequireWindowTarget();
        commands.Retire(); epoch++;
        opened.Sources.ItemsSource = commands.Choices;
        opened.Confirm.IsEnabled = false;
        opened.Preview.Text = broker.Status(target);
    }

    private void RequireNativeView(SessionInstructionScopeView opened)
    {
        if (!ReferenceEquals(window, opened) || !opened.IsNativePresented)
        { throw new InvalidOperationException("The original session instruction controls retired. Explicitly open fresh controls for the intended Active session."); }
    }

    internal Task<string> ExecuteAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token) => ExecuteCoreAsync(command, origin, admitted, null, token);

    private Task<string> ExecuteCoreAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, ArtifactDefinition? selectedInstruction, CancellationToken token)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { return Dispatcher.UIThread.InvokeAsync(() => ExecuteCoreAsync(command, origin, admitted, selectedInstruction, token)); }
        if (!Operation.IsCompleted) { throw new InvalidOperationException("Session instruction read is still owned; wait for completion."); }
        Open();
        var opened = window!;
        var target = RequireWindowTarget();
        var selection = model()!.CaptureDraftPresentationEligibility();
        var attempt = epoch;
        var host = captureHost();
        var native = opened.CapturePresentation();
        bool HostLive() => !disposed && admitted() && host();
        bool ReviewLive() => HostLive() && native() && window == opened && attempt == epoch && selection() && Target() == target;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var running = RunAsync(command, target, origin, HostLive, ReviewLive, opened, cancellation, selectedInstruction);
        Operation = running;
        return running;
    }

    private async Task<string> RunAsync(SessionCommand command, SessionWorkspaceEntry target, RequestOrigin origin,
        Func<bool> host, Func<bool> live, SessionInstructionScopeView opened, CancellationTokenSource source,
        ArtifactDefinition? selectedInstruction)
    {
        try
        {
            var text = command.Operation == SessionCommandOperation.InstructionScopeReview && selectedInstruction is not null
                ? await commands.ReviewAsync(target, selectedInstruction, origin, host, live, source.Token)
                : await commands.ExecuteAsync(command, target, origin, host, live, source.Token);
            if (!live()) { commands.Retire(); throw new OperationCanceledException("Original scope presentation changed."); }
            opened.Preview.Text = text;
            opened.Confirm.IsEnabled = commands.Pending?.CanConfirm == true;
            return text;
        }
        finally { if (ReferenceEquals(cancellation, source)) { cancellation = null; } source.Dispose(); }
    }

    private async Task RunNativeAsync(SessionInstructionScopeView originalView, SessionCommand command, ArtifactDefinition? selectedInstruction = null)
    {
        try
        {
            RequireNativeView(originalView);
            await ExecuteCoreAsync(command, RequestOrigin.LocalUi, captureHost(), selectedInstruction, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (ReferenceEquals(window, originalView))
            {
                commands.Retire();
                if (originalView.IsNativePresented && canReveal())
                { originalView.Confirm.IsEnabled = false; originalView.Preview.Text = "Unconfirmed: " + exception.Message + " No retry or fallback."; }
            }
            reportFailure("Session instruction operation unconfirmed. Refresh exact source and session before trying again.");
        }
    }

    internal void RevokeReview()
    {
        epoch++; commands.Retire(); cancellation?.Cancel();
        if (window is { } opened) { opened.Confirm.IsEnabled = false; opened.Preview.Text = string.Empty; }
    }

    internal void RevokeAll() { broker.RetireAll(); RevokeReview(); window?.Close(); }
    internal async Task ShutdownAsync()
    {
        disposed = true;
        await QuiesceAsync();
    }

    internal async Task QuiesceAsync()
    {
        RevokeAll();
        await JoinAsync();
#pragma warning disable VSTHRD003 // Also join retired hosted views after their actual source reads terminate.
        await CloseOperation;
#pragma warning restore VSTHRD003
    }

    internal void Retire() { disposed = true; RevokeAll(); }

    private async Task JoinAsync()
    {
        try
        {
#pragma warning disable VSTHRD003
            await Operation;
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { reportFailure("Session instruction read terminated without confirmation; no late scope was accepted."); }
    }
}
