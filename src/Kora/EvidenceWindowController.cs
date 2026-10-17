using Kora.Application.Diagnostics;
using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class EvidenceWindowController(
    MainViewModel main, DurableEvidenceQuery query, IEvidenceQueryAccess access,
    ILogger<EvidenceViewModel> logger) : IDisposable
{
    private EvidenceWindow? window;
    private bool disposed;

    internal void Bind() => main.PrivacyClosureRequested += OnPrivacyClosure;

    internal void Open()
    {
        if (disposed || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("Evidence inspection denied: live desktop ownership or privacy admission is unavailable.");
            return;
        }
        if (window is null)
        {
            EvidenceWindow opened;
            var model = new EvidenceViewModel(query, () => !disposed && access.CanInspect, logger);
            try { opened = new(model, main); }
            catch (InvalidOperationException)
            {
                model.Close();
                main.ReportHostInteractionFailure(
                    "Evidence reader unavailable: reader text scale is unconfirmed or preference evidence changed. Inspect saved preference evidence before explicit repair.");
                return;
            }
            opened.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { window = null; } };
            window = opened;
            opened.Show();
        }
        window.Activate();
    }

    private void OnPrivacyClosure(object? sender, EventArgs args) => window?.Close();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        window?.Close();
        window = null;
    }
}
