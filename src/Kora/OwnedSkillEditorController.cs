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
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<OwnedSkillEditorController> logger,
    OwnedSkillInvocationController? invocation = null, OwnedSkillGuidanceRefinementService? refinement = null,
    OwnedSkillFixtureWorkflow? fixtures = null) : IAsyncDisposable
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
        generatedBase = null;
        lifetime?.Cancel();
        CancelRefinement();
        CancelFixture();
        invocation?.Retire();
        window?.RetireReview();
    }

    private bool Eligible() => ownsHost() && owner == ownershipRevision() && original?.IsCurrent() == true
        && lifetime?.IsCancellationRequested == false;

    private async Task Handle(OwnedSkillAuthoringCommand command, OwnedSkillAuthoringAuthority authority)
    {
        if (command.Operation is "cancel-refinement" or "cancel-test")
        {
            if (command.Operation is "cancel-test") { CancelFixture(); }
            else { CancelRefinement(); }
#pragma warning disable VSTHRD003 // Keep the owned model operation until its actual resources terminate.
            await work;
#pragma warning restore VSTHRD003
            return;
        }
        if (command.Operation is "cancel")
        {
            Retire();
#pragma warning disable VSTHRD003 // Join the one owned operation after retiring authority, even if I/O ignores cancellation.
            await work;
#pragma warning restore VSTHRD003
            if (invocation is not null) { await invocation.CloseAndWait(); }
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
                generatedBase = null;
                CancelRefinement();
                CancelFixture();
                invocation?.Retire();
                window?.RetireReview();
            };
            window.CritiqueChanged += () =>
            {
                CancelRefinement();
                CancelFixture();
                var captured = main.CaptureInstructionRefinementEligibility();
                critiqueAuthority = new(RequestOrigin.LocalUi, () => Eligible() && captured());
            };
            window.FixtureChanged += () =>
            {
                CancelFixture();
                var captured = main.CaptureOwnedSkillFixtureEligibility();
                fixtureAuthority = new(RequestOrigin.LocalUi, () => Eligible() && captured());
            };
            window.Saved.SelectionChanged += (_, _) => CancelFixture();
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
        if (command.Operation is "cancel-refinement") { CancelRefinement(); return; }
        if (command.Operation is "cancel-test")
        { CancelFixture(); window?.Report("SIMULATED cancellation requested. Ownership is retained until actual model work joins."); return; }
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
                    opened.Tool.SelectedIndex = 0;
                    opened.InputFormat.SelectedIndex = 0;
                    opened.Critique.Text = "";
                    opened.Fixture.Text = "";
                    fixtureAuthority = null;
                    source = null;
                    initialAuthority = null;
                    generatedBase = null;
                    opened.Purpose.Text = "";
                    opened.ExpectedInput.Text = "";
                    opened.ExpectedOutput.Text = "";
                    CancelRefinement();
                    dirty = false;
                    uncertain = false;
                    opened.Report("New volatile declarative draft. Supply metadata, exact host capability selection and literal guidance, then Validate.");
                    break;
                case "generate-request":
                    if (!await DiscardOrSave()) { return; }
                    PrepareInitial(opened, command.Literal!, authority);
                    break;
                case "expected-input": opened.ExpectedInput.Text = command.Literal; break;
                case "expected-output": opened.ExpectedOutput.Text = command.Literal; break;
                case "generate": await ProposeGuidance(opened, authority, GuidanceOperation.Generate, lifetime.Token); break;
                case "name": opened.NameInput.Text = command.Literal; break;
                case "description": opened.DescriptionInput.Text = command.Literal; break;
                case "guidance": opened.Guidance.Text = command.Literal; break;
                case "critique":
                    CancelRefinement();
                    opened.Critique.Text = command.Literal;
                    critiqueAuthority = authority;
                    break;
                case "refine": await ProposeGuidance(opened, authority, GuidanceOperation.Refine, lifetime.Token); break;
                case "fixture":
                    CancelFixture();
                    opened.Fixture.Text = command.Literal;
                    fixtureAuthority = authority;
                    break;
                case "test": await TestFixture(opened, authority, lifetime.Token); break;
                case "inputs":
                    opened.InputFormat.SelectedIndex = command.Literal switch
                    {
                        "none" => 0,
                        "required-literal" => 1,
                        _ => throw new InvalidOperationException("Select literal guidance or required literal inputs."),
                    };
                    break;
                case "tool":
                    opened.Tool.SelectedIndex = command.Literal switch
                    {
                        "web-page-get" => 1,
                        "none" => 0,
                        _ => throw new InvalidOperationException("Select only no tools or host WebPageGet."),
                    };
                    break;
                case "validate":
                    if (initialAuthority is not null && pendingRefinement is null)
                    { throw new InvalidOperationException("Generate and review the complete initial proposal before validation. Nothing saved."); }
                    validated = pendingRefinement is { } proposal
                        ? proposal.IsCurrent() ? proposal.Proposed : throw new InvalidOperationException("The refinement expired. Generate and review again.")
                        : opened.Document();
                    opened.Report("Valid native TWO-file package" + (pendingRefinement is null ? "" : " from the reviewed guidance proposal")
                        + ". No publication, enablement, tool permission or invocation.");
                    break;
                case "save": await Save(); break;
                case "list":
                    var rows = await workflow.List(original!, lifetime.Token);
                    if (Eligible()) { opened.Saved.ItemsSource = rows; opened.Report("Saved immutable inactive versions. Select one and Reopen or Use; nothing is enabled or invoked."); }
                    break;
                case "use":
                    var selection = opened.Saved.SelectedItem as OwnedDeclarativeSkillSnapshot;
                    var useId = command.Id ?? selection?.Document.Id;
                    var useVersion = command.Version ?? selection?.Document.Version;
                    if (useId is null || useVersion is null)
                    { throw new InvalidOperationException("Select an exact saved ID/version before Use."); }
                    if (invocation is null) { throw new InvalidOperationException("Owned skill invocation is unavailable in this host."); }
                    await invocation.Handle(new("use", useId, useVersion), authority, Eligible);
                    break;
                case "enable":
                case "targets":
                case "next-targets":
                case "target":
                case "request":
                case "input":
                case "url":
                case "review-tool":
                case "review-use":
                case "invoke":
                case "close-use":
                    if (invocation is null) { throw new InvalidOperationException("Owned skill invocation is unavailable in this host."); }
                    await invocation.Handle(command, authority, Eligible);
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
        var saved = pendingRefinement is { } proposal
            ? await refinement!.Save(proposal, opened, Current, lifetime.Token)
            : await workflow.Save(document, original, Current, opened, lifetime.Token);
        if (saved is null || !Current()) { return false; }
        ShowSaved(opened, saved);
        opened.Report("Both exact files durably read back. Saved inactive/unselected; nothing invoked. List and select Use for separate exact-version review.");
        return true;
    }

    private void ShowSaved(OwnedSkillEditorWindow opened, OwnedDeclarativeSkillSnapshot saved)
    {
        CancelRefinement();
        source = saved;
        initialAuthority = null;
        generatedBase = null;
        opened.IdInput.Text = saved.Document.Id;
        opened.VersionInput.Text = saved.Document.Version;
        opened.NameInput.Text = saved.Document.Name;
        opened.DescriptionInput.Text = saved.Document.Description;
        opened.Guidance.Text = saved.Document.Guidance;
        opened.Tool.SelectedIndex = saved.Document.Tool == OwnedSkillToolSelection.WebPageGet ? 1 : 0;
        opened.InputFormat.SelectedIndex = saved.Document.InputTemplate is null ? 0 : 1;
        opened.Critique.Text = "";
        opened.Fixture.Text = "";
        fixtureAuthority = null;
        validated = null;
        dirty = false;
        uncertain = false;
        opened.Report("Exact saved version reopened. It remains inactive; Use requires separate review and enablement. Existing versions cannot be changed; Create a fresh ID/version.");
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
        if (invocation is not null) { await invocation.CloseAndWait(); }
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
        if (invocation is not null) { await invocation.DisposeAsync(); }
        await admission.DisposeAsync();
        lifetime?.Dispose();
        lifetime = null;
    }
}
