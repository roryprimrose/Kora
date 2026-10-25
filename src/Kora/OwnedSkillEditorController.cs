using System.ComponentModel;
using Avalonia.Controls;
using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class OwnedSkillEditorController(
    MainViewModel main, OwnedSkillSaveWorkflow workflow, OwnedSkillControlAdmission admission,
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<OwnedSkillEditorController> logger) : IAsyncDisposable
{
    private OwnedSkillEditorWindow? window;
    private OwnedSkillAuthoringAuthority? original;
    private CancellationTokenSource? lifetime;
    private Task work = Task.CompletedTask;
    private long generation;
    private long owner;
    private bool dirty;
    private bool closing;
    private bool uncertain;
    private bool busy;
    private OwnedDeclarativeSkillDocument? validated;

    internal void Bind()
    {
        main.BindOwnedSkillAuthoring(Handle, PrepareClose);
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnMainChanged;
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CallPolicyRevision) or nameof(MainViewModel.IsPrivacyPresentationHeld))
        { Retire(); }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args)
    {
        Retire();
        window?.Hide();
    }

    private void Retire()
    {
        original?.Retire();
        lifetime?.Cancel();
        window?.RetireReview();
    }

    private bool Eligible() => ownsHost() && owner == ownershipRevision() && original?.IsCurrent() == true
        && lifetime?.IsCancellationRequested == false;

    private async Task Handle(OwnedSkillAuthoringCommand command, OwnedSkillAuthoringAuthority authority)
    {
        if (command.Operation is "cancel")
        {
            Retire();
#pragma warning disable VSTHRD003 // Join the one owned operation after retiring authority, even if I/O ignores cancellation.
            await work;
#pragma warning restore VSTHRD003
            return;
        }
        if (busy) { main.ReportHostInteractionFailure("Skill authoring is busy; cancel or wait for its owned I/O to finish."); return; }
        if (window is null)
        {
            owner = ownershipRevision();
            original = authority;
            lifetime = new();
            window = new(action => Start(action is "create"
                ? new(action, window?.IdInput.Text, window?.VersionInput.Text)
                : new(action), new(RequestOrigin.LocalUi, Eligible)), Eligible);
            window.DraftChanged += () =>
            {
                Interlocked.Increment(ref generation);
                dirty = true;
                validated = null;
                window?.RetireReview();
            };
            window.Closing += OnClosing;
            window.Show();
        }
        if (!Eligible() || !authority.IsCurrent())
        {
            var capture = main.CaptureDefinitionObservationEligibility();
            var capturedOwner = ownershipRevision();
            if (window.ShowForPrivateRecovery(() => authority.IsCurrent() && ownsHost() && capture()
                && capturedOwner == ownershipRevision()))
            { window.Report("Original skill authoring authority expired. Close/discard this draft; recovery never readmits it."); }
            main.ReportHostInteractionFailure("Original skill authoring authority expired. Close/discard this draft; recovery never readmits it.");
            return;
        }
        window.Activate();
        Start(command, authority);
#pragma warning disable VSTHRD003 // Join the controller-owned command, including cancellation-ignoring publication.
        await work;
#pragma warning restore VSTHRD003
    }

    private void Start(OwnedSkillAuthoringCommand command, OwnedSkillAuthoringAuthority authority)
    {
        if (command.Operation is "cancel") { Retire(); return; }
        if (busy) { window?.Report("Wait for owned skill I/O to terminate; the draft remains owned."); return; }
        work = Run(command, authority);
    }

    private async Task Run(OwnedSkillAuthoringCommand command, OwnedSkillAuthoringAuthority authority)
    {
        if (window is not { } opened || lifetime is null) { return; }
        busy = true;
        try
        {
            if (!Eligible() || !authority.IsCurrent()) { throw new InvalidOperationException("Original authoring authority expired."); }
            switch (command.Operation)
            {
                case "editor": break;
                case "create":
                    if (!await DiscardOrSave()) { return; }
                    opened.IdInput.Text = command.Id ?? "";
                    opened.VersionInput.Text = command.Version ?? "1.0.0";
                    opened.NameInput.Text = "";
                    opened.DescriptionInput.Text = "";
                    opened.Guidance.Text = "";
                    dirty = false;
                    uncertain = false;
                    opened.Report("New volatile instruction-only draft. Supply metadata and literal guidance, then Validate.");
                    break;
                case "name": opened.NameInput.Text = command.Literal; break;
                case "description": opened.DescriptionInput.Text = command.Literal; break;
                case "guidance": opened.Guidance.Text = command.Literal; break;
                case "validate":
                    validated = opened.Document();
                    opened.Report("Valid native v1 TWO-file package. No publication, enablement or invocation.");
                    break;
                case "save": await Save(); break;
                case "list":
                    var rows = await workflow.List(original!, lifetime.Token);
                    if (Eligible()) { opened.Saved.ItemsSource = rows; opened.Report("Saved immutable inactive versions. Select one and Reopen; nothing is enabled or invocable."); }
                    break;
                case "reopen":
                    var selected = opened.Saved.SelectedItem as OwnedDeclarativeSkillSnapshot;
                    var id = command.Id ?? selected?.Document.Id;
                    var version = command.Version ?? selected?.Document.Version;
                    if (id is null || version is null) { throw new InvalidOperationException("Select an exact saved version before reopening."); }
                    if (!await DiscardOrSave()) { return; }
                    var saved = await workflow.Read(id, version, original!, lifetime.Token)
                        ?? throw new InvalidDataException("The exact saved skill version is absent.");
                    if (!Eligible()) { return; }
                    ShowSaved(opened, saved);
                    break;
                default: throw new InvalidOperationException(OwnedSkillAuthoringCommand.Syntax);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (exception is OwnedSkillPublicationUnconfirmedException) { uncertain = true; }
            AuthoringFailed(logger, exception.GetType().Name);
            opened.Report(exception is OwnedSkillPublicationUnconfirmedException
                ? "Publication may have occurred. Do not retry Save. List/Reopen the exact ID/version to reconcile; no rollback or invocation is claimed."
                : exception is OperationCanceledException ? "Cancelled. Owned I/O has joined; no saved result is claimed."
                : "Skill authoring failed: " + exception.Message);
        }
        finally { busy = false; }
    }

    private async Task<bool> Save()
    {
        if (window is not { } opened || lifetime is null || original is null) { return false; }
        if (uncertain) { throw new InvalidOperationException("Publication is uncertain. Reopen the exact saved version; do not retry."); }
        var document = validated ?? throw new InvalidOperationException("Validate this exact draft before Save.");
        var exact = Interlocked.Read(ref generation);
        bool Current() => Eligible() && exact == Interlocked.Read(ref generation) && ReferenceEquals(validated, document);
        var saved = await workflow.Save(document, original, Current, opened, lifetime.Token);
        if (saved is null || !Current()) { return false; }
        ShowSaved(opened, saved);
        opened.Report("Both exact files durably read back. Saved inactive/unselected, invocation unavailable. List and Reopen selects this immutable saved version.");
        return true;
    }

    private void ShowSaved(OwnedSkillEditorWindow opened, OwnedDeclarativeSkillSnapshot saved)
    {
        opened.IdInput.Text = saved.Document.Id;
        opened.VersionInput.Text = saved.Document.Version;
        opened.NameInput.Text = saved.Document.Name;
        opened.DescriptionInput.Text = saved.Document.Description;
        opened.Guidance.Text = saved.Document.Guidance;
        validated = null;
        dirty = false;
        uncertain = false;
        opened.Report("Exact saved version reopened. It remains inactive and cannot be invoked. Existing versions cannot be changed; Create a fresh ID/version.");
    }

    private async Task<bool> DiscardOrSave()
    {
        if (!dirty || window is null || lifetime is null) { return true; }
        var capture = main.CaptureDefinitionObservationEligibility();
        var capturedOwner = ownershipRevision();
        bool PrivateEligible() => ownsHost() && capture() && ownershipRevision() == capturedOwner;
        if (!window.IsVisible && !window.ShowForPrivateRecovery(PrivateEligible)) { return false; }
        var choice = await window.DirtyChoice(Eligible(),
            PrivateEligible, CancellationToken.None);
        if (choice == 1) { dirty = false; return true; }
        return choice == 0 && await Save();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closing) { return; }
        args.Cancel = true;
        if (busy) { Retire(); return; }
        work = CloseWindow();
    }

    private async Task CloseWindow()
    {
        try { if (!await PrepareClose()) { return; } }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { AuthoringFailed(logger, exception.GetType().Name); window?.Report("Closing failed; the draft remains open."); }
    }

    internal async Task<bool> PrepareClose()
    {
        if (window is null) { return true; }
        if (busy)
        {
            Retire();
#pragma warning disable VSTHRD003 // Cancellation closes authority immediately; ownership persists until actual I/O terminates.
            await work;
#pragma warning restore VSTHRD003
        }
        if (!await DiscardOrSave()) { return false; }
        Retire();
        closing = true;
        window.Close();
        window = null;
        original = null;
        lifetime?.Dispose();
        lifetime = null;
        closing = false;
        return true;
    }

#pragma warning disable VSTHRD100 // Framework exit event boundary; failures are explicitly reported without escaping the event.
    internal async void OnHostExit(object? sender, Avalonia.Controls.ApplicationLifetimes.ControlledApplicationLifetimeExitEventArgs args)
    {
        try { await DisposeAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { AuthoringFailed(logger, exception.GetType().Name); }
    }
#pragma warning restore VSTHRD100

    public async ValueTask DisposeAsync()
    {
        Retire();
#pragma warning disable VSTHRD003 // Application exit retains resource ownership until the actual operation terminates.
        await work;
#pragma warning restore VSTHRD003
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.PropertyChanged -= OnMainChanged;
        main.BindOwnedSkillAuthoring(null, null);
        await admission.DisposeAsync();
        lifetime?.Dispose();
    }
}
