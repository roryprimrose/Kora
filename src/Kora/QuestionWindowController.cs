using Avalonia.Controls;

using Kora.Application;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class QuestionWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly NativeQuestionHost host;
    private readonly DurableVersionQuery query;
    private readonly IApplicationInfo info;
    private readonly NativeDetailRenderer renderer;
    private readonly ILogger<QuestionWindowController> logger;
    private QuestionWindow? window;
    private NativeQuestionViewModel? state;
    private bool running;
    private bool disposed;

    internal QuestionWindowController(MainViewModel main, NativeQuestionHost host, DurableVersionQuery query,
        IApplicationInfo info, NativeDetailRenderer renderer, Func<bool> ownsDesktop, ILogger<QuestionWindowController> logger)
    {
        this.main = main;
        this.host = host;
        this.query = query;
        this.info = info;
        this.renderer = renderer;
        this.logger = logger;
        host.BindGate(() => !disposed && ownsDesktop() && main.CanRevealPrivatePresentation);
        host.BindCallObservation(() => main.CallObservation);
        main.PrivacyClosureRequested += OnPrivacyClosure;
    }

    internal async Task ReviewVersionAsync(Window owner)
    {
        if (disposed || !main.CanRevealPrivatePresentation)
        {
            main.ReportHostInteractionFailure("Native question denied: privacy or host lifecycle admission is unavailable. Return through the tray after resolving the blocker; no query was dispatched.");
            return;
        }
        if (running) { window?.Activate(); return; }
        window?.Close();
        running = true;
        try
        {
            var version = string.Empty;
            var receipt = await query.RunAsync(Kora.Core.Hosting.RequestOrigin.LocalUi, async () =>
            {
                await host.AskVersionAsync(async model =>
                {
                    state = model;
                    model.RefreshEligibility();
                    if (!model.IsEditable) { return; }
                    window = new(model, renderer);
                    var opened = window;
                    opened.Closed += (_, _) =>
                    {
                        if (ReferenceEquals(window, opened))
                        {
                            window = null;
                            state = null;
                        }
                    };
                    window.Show(owner);
                    window.Activate();
                    await model.Completion;
                }, CancellationToken.None);
                if (!main.CanRevealPrivatePresentation) { throw new InvalidOperationException("Privacy closed before the local query."); }
                version = info.Version;
            }, CancellationToken.None);
            if (!disposed && main.CanRevealPrivatePresentation)
            {
                window?.ReportOutcome($"Local version: {version}\n{DurableVersionQuery.StorageDisclosure}"
                    + $"\nVerified durable receipt: {receipt.State}. No microphone, model, grant or effect was used.");
            }
        }
        catch (OperationCanceledException)
        {
            window?.ReportOutcome("Question cancelled. No version returned or successful receipt claimed. Incomplete durable work recovers as Unknown, without replay.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            const string failure = "Native version review failed. No successful query receipt or approval is claimed. Close and start a fresh review; uncertain work is not replayed.";
            if (window is not null) { window.ReportOutcome(failure); }
            else { main.ReportHostInteractionFailure(failure); }
            QueryFailure(logger, exception.GetType().FullName);
        }
        finally { running = false; }
    }

    private void OnPrivacyClosure(object? sender, EventArgs args)
    {
        state?.Close("Privacy closed. The original question cannot be answered through this window.");
        window?.Close();
        window = null;
        state = null;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        host.BindGate(static () => false);
        OnPrivacyClosure(this, EventArgs.Empty);
    }
}
