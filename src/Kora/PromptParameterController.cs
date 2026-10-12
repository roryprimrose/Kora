using System.ComponentModel;
using Avalonia.Threading;
using Kora.Application.Definitions;
using Kora.Application.ViewModels;
using Kora.Core.Artifacts;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class PromptParameterController(
    MainViewModel main, ArtifactCatalogue catalogue, PromptRequestConsumer consumer,
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<PromptParameterController> logger) : IDisposable
{
    private PromptParameterWindow? window;
    private PromptParameterDraft? draft;
    private CancellationTokenSource? lifetime;
    private long ownership;
    private bool busy;
    private bool disposed;

    internal void Bind()
    {
        main.PromptPreparationRequested += Open;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnInputChanged;
        catalogue.Changed += OnCatalogueChanged;
        catalogue.ReloadFailed += OnReloadFailed;
    }

    private Task Open(PromptParameterDraft proposed)
    {
        Close();
        if (disposed || !ownsHost() || !proposed.IsCurrent)
        {
            proposed.Dispose();
            main.ReportHostInteractionFailure("Prompt preparation requires current private ownership and exact enabled source.");
            return Task.CompletedTask;
        }
        ownership = ownershipRevision();
        draft = proposed;
        lifetime = new();
        window = new(proposed, Edit, action => _ = Run(action));
        var opened = window;
        window.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { Clear(); } };
        window.Show();
        return Task.CompletedTask;
    }

    private bool Eligible(PromptParameterDraft captured) => !disposed && ownsHost()
        && ownershipRevision() == ownership && ReferenceEquals(draft, captured) && captured.IsCurrent;

    private void Edit(string name, string value)
    {
        window?.RetirePreview();
        if (draft is null) { return; }
        try { draft.SetValue(name, value); window!.Status.Text = "Input changed. Preview the whole expanded request again."; }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        { window!.Status.Text = exception.Message; }
    }

    private async Task Run(string action)
    {
        if (busy || draft is not { } captured || window is not { } opened || lifetime is null) { return; }
        var token = lifetime.Token;
        busy = true;
        try
        {
            if (!Eligible(captured)) { Close(); return; }
            if (string.Equals(action, "Preview", StringComparison.Ordinal))
            {
                opened.RetirePreview();
                var expanded = await captured.Preview(main.IsPromptSourceCurrent, token);
                if (!Eligible(captured)) { return; }
                opened.Expanded.Text = expanded.UserRequest;
                opened.Use.IsEnabled = captured.CanUse;
                opened.Status.Text = $"Complete expanded USER REQUEST; digest {expanded.Digest}. Preview is not model/tool approval. Use is separate.";
            }
            else
            {
                var expanded = await captured.CaptureUse(main.IsPromptSourceCurrent, token);
                bool Current() => Eligible(captured) && captured.IsUseCurrent(expanded);
                var result = await consumer.Use(expanded, Current, token);
                if (!Current()) { return; }
                main.ReportPromptUse(result);
                opened.Status.Text = $"Generation unavailable: {result.Outcome} / {result.Reason}. No model or action started; inputs remain ephemeral.";
            }
        }
        catch (OperationCanceledException) { if (ReferenceEquals(window, opened)) { opened.RetirePreview(); } }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            PreparationFailed(logger, exception.GetType().Name);
            if (ReferenceEquals(window, opened))
            {
                opened.RetirePreview();
                opened.Status.Text = exception is InvalidDataException or InvalidOperationException
                    ? exception.Message : "Prompt preparation unavailable. Cancel and reselect the exact reviewed revision.";
            }
        }
        finally { busy = false; }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) => DispatchClose();
    private void OnInputChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CommandText) or nameof(MainViewModel.CallPolicyRevision))
        { DispatchClose(); }
    }
    private void OnCatalogueChanged(object? sender, EventArgs args) => DispatchClose();
    private void OnReloadFailed(object? sender, ArtifactCatalogueReloadFailedEventArgs args) => DispatchClose();

    private void DispatchClose()
    {
        var opened = window;
        if (Dispatcher.UIThread.CheckAccess()) { Close(); }
        else { Dispatcher.UIThread.Post(() => { if (ReferenceEquals(window, opened)) { Close(); } }); }
    }

    private void Close()
    {
        var opened = window;
        Clear();
        opened?.Close();
    }

    private void Clear()
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        draft?.Dispose();
        draft = null;
        var opened = window;
        window = null;
        opened?.ClearPrivateText();
    }

    public void Dispose()
    {
        disposed = true;
        main.PromptPreparationRequested -= Open;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.PropertyChanged -= OnInputChanged;
        catalogue.Changed -= OnCatalogueChanged;
        catalogue.ReloadFailed -= OnReloadFailed;
        Close();
    }
}
