using System.ComponentModel;
using Kora.Application.ViewModels;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class MicrophoneRecoveryWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly ILogger<MicrophoneRecoveryWindow> logger;
    private MicrophoneRecoveryWindow? window;
    private bool disposed;

    internal MicrophoneRecoveryWindowController(MainViewModel main, ILogger<MicrophoneRecoveryWindow> logger)
    {
        this.main = main;
        this.logger = logger;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        main.PropertyChanged += OnHostChanged;
    }

    internal void Open()
    {
        if (disposed || !main.CanUseTrayMicrophoneRecovery)
        {
            main.ReportHostInteractionFailure("Microphone recovery unavailable: return to the owning unlocked host and refresh. No consent or enablement was granted.");
            return;
        }
        if (window is null)
        {
            var model = new MicrophoneRecoveryViewModel(main);
            var opened = new MicrophoneRecoveryWindow(model, main.RequestVoiceRecovery, logger);
            opened.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { window = null; } };
            window = opened;
            opened.Show();
        }
        window.Activate();
    }

    private void OnPrivacyClosure(object? sender, EventArgs args) => window?.Close();
    private void OnHostChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!main.CanUseTrayMicrophoneRecovery) { window?.Close(); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        main.PropertyChanged -= OnHostChanged;
        window?.Close();
        window = null;
    }
}
