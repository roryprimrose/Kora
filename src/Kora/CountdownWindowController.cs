using System.ComponentModel;

using Kora.Application.Interaction;
using Kora.Application.ViewModels;

namespace Kora;

internal sealed class CountdownWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly CountdownWorkflow workflow;
    private CountdownWindow? window;
    private bool disposed;

    internal CountdownWindowController(MainViewModel main, CountdownWorkflow workflow)
    {
        this.main = main;
        this.workflow = workflow;
        main.BindCountdown(workflow);
        main.CountdownRequested += Open;
        main.PrivacyClosureRequested += ClosePrivate;
        main.PropertyChanged += OnMainChanged;
        workflow.PropertyChanged += OnMainChanged;
    }

    private void Open(object? sender, EventArgs args)
    {
        if (disposed || !main.CanRunCountdown)
        {
            main.ReportHostInteractionFailure("Timer unavailable: native private ownership was revoked.");
            return;
        }
        if (window is null)
        {
            var opened = new CountdownWindow(main, workflow);
            opened.Closed += (_, _) =>
            {
                workflow.Retire();
                if (ReferenceEquals(window, opened)) { window = null; }
            };
            window = opened;
            opened.Show();
        }
        window.Activate();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!main.CanRunCountdown) { ClosePrivate(sender, EventArgs.Empty); }
    }

    private void ClosePrivate(object? sender, EventArgs args)
    {
        workflow.Retire();
        window?.Close();
        window = null;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.CountdownRequested -= Open;
        main.PrivacyClosureRequested -= ClosePrivate;
        main.PropertyChanged -= OnMainChanged;
        workflow.PropertyChanged -= OnMainChanged;
        main.BindCountdown(null);
        window?.Close();
        window = null;
    }
}
