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
    private SessionInstructionScopeWindow? window;
    private CancellationTokenSource? cancellation;
    private long epoch;
    private bool disposed;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal Task CloseOperation { get; private set; } = Task.CompletedTask;
    internal SessionInstructionScopeWindow? Window => window;

    internal void Open()
    {
        if (disposed || !canReveal()) { throw new InvalidOperationException("Private session scope presentation is unavailable."); }
        _ = Target();
        if (window is null)
        {
            var opened = new SessionInstructionScopeWindow();
            window = opened;
            opened.Sources.SelectionChanged += (_, _) => { commands.Retire(); opened.Confirm.IsEnabled = false; opened.Preview.Text = "Selection changed. Review the complete source before confirming."; epoch++; };
            opened.Refresh.Click += (_, _) =>
            {
                try { Refresh(opened); }
                catch (InvalidOperationException exception)
                {
                    opened.Preview.Text = "Unconfirmed: " + exception.Message;
                    reportFailure(exception.Message);
                }
            };
            opened.Review.Click += async (_, _) => await RunNativeAsync(new(SessionCommandOperation.InstructionScopeReview)
            { ScopeInstructionName = (opened.Sources.SelectedItem as ArtifactDefinition)?.Name });
            opened.Confirm.Click += async (_, _) => await RunNativeAsync(new(SessionCommandOperation.InstructionScopeConfirm));
            opened.Stop.Click += async (_, _) => await RunNativeAsync(new(SessionCommandOperation.InstructionScopeStop));
            opened.Status.Click += async (_, _) => await RunNativeAsync(new(SessionCommandOperation.InstructionScopeStatus));
            opened.Closed += async (_, _) =>
            {
                if (ReferenceEquals(window, opened)) { window = null; }
                RevokeReview();
                await (CloseOperation = JoinAsync());
                opened.Preview.Text = string.Empty;
                opened.Sources.ItemsSource = null;
            };
            Refresh(opened);
            var parent = owner();
            if (parent is null) { opened.Show(); } else { opened.Show(parent); }
        }
        window.Activate();
    }

    private SessionWorkspaceEntry Target() => model()?.SelectedSessionRecord is { Authority.IsActive: true } target
        ? target : throw new InvalidOperationException("Select the exact Active session in Sessions first.");

    private void Refresh(SessionInstructionScopeWindow opened)
    {
        commands.Retire(); epoch++;
        opened.Sources.ItemsSource = commands.Choices;
        opened.Confirm.IsEnabled = false;
        opened.Preview.Text = broker.Status(Target());
    }

    internal Task<string> ExecuteAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { return Dispatcher.UIThread.InvokeAsync(() => ExecuteAsync(command, origin, admitted, token)); }
        if (!Operation.IsCompleted) { throw new InvalidOperationException("Session instruction read is still owned; wait for completion."); }
        Open();
        var opened = window!;
        var target = Target();
        var selection = model()!.CaptureDraftPresentationEligibility();
        var attempt = epoch;
        var host = captureHost();
        bool HostLive() => !disposed && admitted() && host();
        bool ReviewLive() => HostLive() && window == opened && attempt == epoch && selection() && Target() == target;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var running = RunAsync(command, target, origin, HostLive, ReviewLive, opened, cancellation);
        Operation = running;
        return running;
    }

    private async Task<string> RunAsync(SessionCommand command, SessionWorkspaceEntry target, RequestOrigin origin,
        Func<bool> host, Func<bool> live, SessionInstructionScopeWindow opened, CancellationTokenSource source)
    {
        try
        {
            var text = await commands.ExecuteAsync(command, target, origin, host, live, source.Token);
            if (!live()) { commands.Retire(); throw new OperationCanceledException("Original scope presentation changed."); }
            opened.Preview.Text = text;
            opened.Confirm.IsEnabled = commands.Pending?.CanConfirm == true;
            return text;
        }
        finally { if (ReferenceEquals(cancellation, source)) { cancellation = null; } source.Dispose(); }
    }

    private async Task RunNativeAsync(SessionCommand command)
    {
        try { await ExecuteAsync(command, RequestOrigin.LocalUi, captureHost(), CancellationToken.None); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            commands.Retire();
            if (window is { } opened && canReveal())
            { opened.Confirm.IsEnabled = false; opened.Preview.Text = "Unconfirmed: " + exception.Message + " No retry or fallback."; }
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
