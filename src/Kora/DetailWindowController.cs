using System.Runtime.InteropServices;

using Avalonia.Controls;

using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class DetailWindowController : IDisposable
{
    private readonly MainViewModel? viewModel;
    private readonly IUserDocumentationProvider documentation;
    private readonly Func<bool> canAccess;
    private readonly NativeDetailRenderer renderer;
    private readonly IDetailClipboard clipboard;
    private readonly Func<DetailViewerState, NativeDocumentResult, Func<bool, int?, int, Task>, IDetailView> createView;
    private readonly ILogger<DetailWindowController> logger;
    private readonly DetailViewerRegistry registry = new();
    private readonly Dictionary<DetailContentReference, (DetailViewerState State, IDetailView View)> windows = [];
    private readonly Dictionary<string, (HostId<EvidenceIdentity> Id, long Revision, string Digest, string Title)> pages =
        new(StringComparer.Ordinal);
    private bool disposed;
    private Func<bool> historyAccess = () => false;
    private Func<DetailContentReference, AdmittedDetailContent?> webResultSource = _ => null;

    internal void BindHistoryAccess(Func<bool> admission) => historyAccess = admission;
    internal void BindWebResultSource(Func<DetailContentReference, AdmittedDetailContent?> resolve) => webResultSource = resolve;
    private bool CanAccess(AdmittedDetailContent content) =>
        canAccess() && (content.Origin != DetailContentOrigin.SessionHistory || historyAccess())
        && (content.Origin != DetailContentOrigin.RetrievedWebResult
            || ReferenceEquals(webResultSource(content.Reference), content));

    internal DetailWindowController(
        IUserDocumentationProvider documentation, MainViewModel viewModel,
        ILogger<DetailWindowController> logger, ILogger<NativeDetailRenderer> rendererLogger)
        : this(documentation, () => viewModel.CanRevealPrivatePresentation,
            new NativeDetailRenderer(rendererLogger), new NativeDetailClipboard(),
            (state, result, copy) => new DetailWindow(state, result, copy), logger)
    {
        this.viewModel = viewModel;
        viewModel.PrivacyClosureRequested += OnPrivacyClosureRequested;
        BindWebResultSource(viewModel.ResolveWebResultDetails);
        viewModel.WebResultDetailsChanged += OnWebResultDetailsChanged;
    }

    internal DetailWindowController(
        IUserDocumentationProvider documentation, Func<bool> canAccess, NativeDetailRenderer renderer,
        IDetailClipboard clipboard, Func<DetailViewerState, NativeDocumentResult, Func<bool, int?, int, Task>, IDetailView> createView,
        ILogger<DetailWindowController> logger)
    {
        this.documentation = documentation;
        this.canAccess = canAccess;
        this.renderer = renderer;
        this.clipboard = clipboard;
        this.createView = createView;
        this.logger = logger;
    }

    // This is a host-only route from the guide's explicit button, never an action/schema/model route.
    internal string OpenEmbeddedPage(UserDocumentationPage page, Window? owner)
    {
        if (disposed || !canAccess()) { return "Details unavailable: the privacy/input gate is closed."; }
        if (!documentation.GetPages().Contains(page))
        {
            return "Details unavailable: this is not the current immutable embedded page.";
        }
        AdmittedDetailContent content;
        try
        {
            content = Admit(page);
        }
        catch (InvalidDataException) { return "Embedded page admission failed; nothing was truncated or opened."; }
        catch (ArgumentException) { return "Embedded page labels or Unicode source are invalid; nothing was opened."; }
        return OpenContent(content, owner);
    }

    internal string OpenHistoryDetail(AdmittedDetailContent content, Window? owner)
    {
        if (content.Origin != DetailContentOrigin.SessionHistory || content.HistorySession is null)
        {
            throw new InvalidDataException("Only freshly resolved retained history may enter this detail route.");
        }

        return OpenContent(content, owner);
    }

    internal string OpenWebResult(DetailContentReference reference, Window? owner)
    {
        var content = webResultSource(reference);
        if (content?.Origin != DetailContentOrigin.RetrievedWebResult || content.WebResult is null
            || content.Reference != reference)
        {
            return "Exact web-result details unavailable: this volatile reference is no longer current.";
        }
        return OpenContent(content, owner);
    }

    private void OnWebResultDetailsChanged(object? sender, EventArgs eventArgs) => RetireUnavailableWebResults();

    internal void RetireUnavailableWebResults()
    {
        foreach (var entry in windows.Where(pair => pair.Value.State.Content is not { } content
            || content.Origin == DetailContentOrigin.RetrievedWebResult && !CanAccess(content)).ToArray())
        {
            windows.Remove(entry.Key);
            registry.Close(entry.Key);
            entry.Value.View.ClearAndClose();
        }
    }

    private string OpenContent(AdmittedDetailContent content, Window? owner)
    {
        if (disposed || !CanAccess(content)) { return "Details unavailable: the privacy/input gate is closed."; }
        DetailViewerState state;
        try
        {
            state = registry.Open(content, CanAccess(content),
                content.Origin == DetailContentOrigin.RetrievedWebResult ? () => CanAccess(content) : null);
        }
        catch (InvalidDataException) { return "Details unavailable: the immutable receipt changed. Close it and refresh history."; }
        catch (InvalidOperationException) { return "Details unavailable: close a viewer or check the privacy/input gate."; }
        if (windows.TryGetValue(content.Reference, out var existing))
        {
            if (!CanAccess(content) || existing.State.Content is null)
            {
                RetireUnavailableWebResults();
                return "Details unavailable: the exact source admission changed.";
            }
            try
            {
                existing.View.Activate();
                return "Activated the existing immutable detail revision.";
            }
            catch (InvalidOperationException)
            {
                windows.Remove(content.Reference);
                registry.Close(content.Reference);
                existing.View.ClearAndClose();
                return "Native detail activation failed; no viewer or private render state was retained.";
            }
        }
        var generation = state.Generation;
        var result = renderer.Render(content.Source, content.Kind, content.Reference);
        if (!state.CompleteRender(generation, result.SemanticText, result.Status, result.IsFallback) || !CanAccess(content))
        {
            registry.Close(content.Reference);
            return "Details unavailable: the privacy/input gate closed.";
        }
        var reference = content.Reference;
        IDetailView view;
        try
        {
            view = createView(state, result,
                (confirmed, selectionStart, selectionLength) => CopyAsync(reference, generation, confirmed, selectionStart, selectionLength));
        }
        catch (InvalidOperationException)
        {
            registry.Close(reference);
            return "Native detail controls unavailable; no viewer or private render state was retained.";
        }
        if (!CanAccess(content) || state.Generation != generation || state.Content is null)
        {
            registry.Close(reference);
            view.ClearAndClose();
            return "Details unavailable: the exact source admission changed before opening.";
        }
        view.Closed += (_, _) =>
        {
            windows.Remove(reference);
            registry.Close(reference);
        };
        windows.Add(reference, (state, view));
        try
        {
            view.ShowOwned(owner);
            if (!CanAccess(content) || state.Generation != generation || state.Content is null)
            {
                windows.Remove(reference);
                registry.Close(reference);
                view.ClearAndClose();
                return "Details unavailable: the exact source admission changed while opening.";
            }
            view.Activate();
        }
        catch (InvalidOperationException)
        {
            registry.Close(reference);
            windows.Remove(reference);
            view.ClearAndClose();
            return "Native detail window unavailable; no viewer or private render state was retained.";
        }
        return result.Status;
    }

    private AdmittedDetailContent Admit(UserDocumentationPage page)
    {
        var item = pages.GetValueOrDefault(page.Id);
        if (item.Id.Value == Guid.Empty) { item = (new(Guid.NewGuid()), 1, string.Empty, string.Empty); }
        var candidate = new AdmittedDetailContent(new(item.Id, item.Revision), DetailContentKind.Markdown,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, page.Title,
            "Immutable embedded Kora documentation; not a session/task result.", page.Markdown);
        if (item.Digest.Length != 0 && (!string.Equals(item.Digest, candidate.Digest, StringComparison.Ordinal)
            || !string.Equals(item.Title, candidate.Title, StringComparison.Ordinal)))
        {
            item.Revision++;
            candidate = new(new(item.Id, item.Revision), candidate.Kind, candidate.Origin, candidate.Sensitivity,
                candidate.Title, candidate.Provenance, candidate.Source);
        }
        pages[page.Id] = (item.Id, item.Revision, candidate.Digest, candidate.Title);
        return candidate;
    }

    internal async Task CopyAsync(DetailContentReference reference, long generation, bool confirmed,
        int? selectionStart = null, int selectionLength = 0)
    {
        if (disposed || !windows.TryGetValue(reference, out var entry)) { return; }
        var state = entry.State;
        if (state.Generation != generation || state.Reference != reference)
        {
            state.ReportStatus("Copy blocked: the requested immutable revision or generation is stale.");
            return;
        }
        string? source;
        var accessible = state.Content is { } content && CanAccess(content);
        var allowed = selectionStart is { } start
            ? state.TryGetCopySelection(accessible, confirmed, start, selectionLength, out source)
            : state.TryGetCopySource(accessible, confirmed, out source);
        if (!allowed) { return; }
        try
        {
            // Revalidate the exact live state immediately before invoking the platform write.
            if (disposed || state.Content is null || !CanAccess(state.Content) || state.Generation != generation
                || !windows.TryGetValue(reference, out var current) || !ReferenceEquals(current.State, state))
            {
                state.ReportStatus("Copy blocked: revision/access/privacy changed.");
                return;
            }
            await clipboard.WritePlainTextAsync(entry.View, source!);
            if (state.Generation == generation && state.Content is not null && CanAccess(state.Content))
            {
                state.ReportStatus(selectionStart is null
                    ? "Exact source copied as Unicode plain text outside Kora."
                    : "Selected text copied as Unicode plain text outside Kora.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException
            or IOException or ExternalException)
        {
            // No exception message or source text enters logs.
            ClipboardFailure(logger, reference.ItemId.Value, reference.Revision, NativeDetailProfile.Name);
            if (state.Generation == generation && state.Content is not null)
            {
                state.ReportStatus("Clipboard write failed; no successful copy is claimed.");
            }
        }
    }

    private void OnPrivacyClosureRequested(object? sender, EventArgs eventArgs) => ClearForPrivacy();

    internal void RevokeSession(HostId<SessionIdentity> session)
    {
        foreach (var entry in windows.Where(pair => pair.Value.State.Content?.HistorySession == session
            || pair.Value.State.Content?.SessionSource?.SessionId == session).ToArray())
        {
            windows.Remove(entry.Key);
            registry.Close(entry.Key);
            entry.Value.View.ClearAndClose();
        }
    }

    internal void ClearForPrivacy()
    {
        registry.ClearForPrivacy();
        var owned = windows.Values.Select(entry => entry.View).ToArray();
        windows.Clear();
        pages.Clear();
        foreach (var view in owned) { view.ClearAndClose(); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        if (viewModel is not null)
        {
            viewModel.PrivacyClosureRequested -= OnPrivacyClosureRequested;
            viewModel.WebResultDetailsChanged -= OnWebResultDetailsChanged;
        }
        ClearForPrivacy();
    }
}
