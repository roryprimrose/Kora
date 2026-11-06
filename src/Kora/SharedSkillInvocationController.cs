using System.ComponentModel;
using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Skills;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SharedSkillInvocationController(
    MainViewModel main, SharedSkillInvocationWorkflow workflow, SkillPackagesWindowController sources,
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<SharedSkillInvocationController> logger,
    ReviewedToolSkillInvocationWorkflow? toolWorkflow = null, QuestionWindowController? questions = null) : IDisposable
{
    private SharedSkillInvocationWindow? window;
    private SharedSkillUseReview? review;
    private SharedSkillReferenceConsent? referenceConsent;
    private SharedSkillInvocation? invocation;
    private CancellationTokenSource? lifetime;
    private Task active = Task.CompletedTask;
    private bool disposed;
    private bool opening;
    private bool showing;
    private bool closeAfterShow;
    private long ownerRevision;
    private long callRevision;
    private long inputRevision;
    private long toolInputRevision;
    private RequestOrigin requestOrigin;
    private Guid? next;
#pragma warning disable VSTHRD003 // Exposes the owned operation for lifecycle observation without starting another submission.
    internal Task ActiveOperation => active;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        sources.BindInvocation((source, snapshot) => Open(source, snapshot, RequestOrigin.LocalUi, static () => true));
        sources.BindToolInvocation((source, snapshot) => Open(source, snapshot, RequestOrigin.LocalUi, static () => true, true));
        main.SharedSkillInvocationRequested += Route;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnMainChanged;
        main.BindSharedSkillUseShutdown(CloseAndWait);
    }

    private bool Eligible() => !disposed && ownsHost() && ownershipRevision() == ownerRevision
        && main.CallPolicyRevision == callRevision && main.CanRevealPrivatePresentation && main.IsSkillReviewCallEligible
        && !main.IsResponseInteractionPending;

    private async Task Open(SharedSkillSource source, SharedSkillSnapshot snapshot, RequestOrigin origin, Func<bool> eligible,
        bool toolCapable = false)
    {
        if (opening) { throw new InvalidOperationException("Wait for the exact source review already opening."); }
        opening = true;
        try
        {
        await CloseAndWait();
        ownerRevision = ownershipRevision();
        callRevision = main.CallPolicyRevision;
        lifetime = new();
        try
        {
            toolMode = toolCapable;
            if (toolCapable)
            {
                if (toolWorkflow is null) { throw new InvalidOperationException("Reviewed tool skill invocation is unavailable in this host."); }
                toolSource = source;
                toolSnapshot = snapshot;
            }
            else if (snapshot.ReferenceDeclaration is not null)
            { referenceConsent = await workflow.ReviewReferenceDeclarationAsync(source, snapshot, () => Eligible(), origin, lifetime.Token); }
            else { review = await workflow.ReviewAsync(source, snapshot, () => Eligible(), origin, lifetime.Token); }
            if (!eligible() || !Eligible()) { Close(); return; }
            requestOrigin = origin;
            window = new(action => Start(action, RequestOrigin.LocalUi), toolCapable, snapshot.McpReference is not null);
            window.Instructions.Text = $"{snapshot.Name} | {snapshot.Version} | source {source.Id:D} | SHA256 {snapshot.RevisionDigest}\n\n"
                + (toolCapable ? snapshot.Text : referenceConsent?.DeclarationText ?? review!.Text);
            window.SetReferenceConsent(referenceConsent is not null);
            window.Closed += (_, _) => Close();
            window.Request.PropertyChanged += (_, args) =>
            { if (args.Property == Avalonia.Controls.TextBox.TextProperty) { InputChanged(); } };
            window.Address.PropertyChanged += (_, args) =>
            { if (args.Property == Avalonia.Controls.TextBox.TextProperty) { InputChanged(); } };
            window.SecondAddress.PropertyChanged += (_, args) =>
            { if (args.Property == Avalonia.Controls.TextBox.TextProperty) { InputChanged(); } };
            window.McpArguments.PropertyChanged += (_, args) =>
            { if (args.Property == Avalonia.Controls.TextBox.TextProperty) { InputChanged(); } };
            window.McpQuestion.PropertyChanged += (_, args) =>
            { if (args.Property == Avalonia.Controls.TextBox.TextProperty) { McpQuestionChanged(); } };
            window.McpServers.SelectionChanged += (_, _) => InputChanged();
            window.McpAccounts.SelectionChanged += (_, _) => InputChanged();
            window.Targets.SelectionChanged += (_, _) =>
            { Interlocked.Increment(ref toolInputRevision); RetireConfirmation(); RetireToolReview(); };
            showing = true;
            try { window.Show(); }
            finally
            {
                showing = false;
                if (closeAfterShow) { closeAfterShow = false; Close(); }
            }
            if (window is null || !Eligible()) { Close(); return; }
            window.Status.Text = toolCapable
                ? "Select an existing Active target, review exact target and tool revision, then separately enable. No action permission granted."
                : referenceConsent is not null
                    ? "Review the exact declaration. Authorize only those local reads, or use 'shared skill review invocation' to confirm this displayed declaration. Enablement remains separate."
                    : "Review all instructions, then separately enable this exact revision. Nothing invoked.";
        }
        catch { Close(); throw; }
        }
        finally { opening = false; }
    }

    internal async Task Route(SharedSkillInvocationCommand command, RequestOrigin origin, Func<bool> eligible)
    {
        try
        {
            if (!eligible())
            {
                RetireMcpAnswer();
                await JoinMcpAnswer();
                throw new InvalidOperationException("Original command admission expired.");
            }
            if (string.Equals(command.Action, "Review", StringComparison.Ordinal))
            {
                var selection = sources.ResolveInvocation(command.Argument)
                    ?? throw new InvalidOperationException("List one registered source natively first. Select its exact case-sensitive name; ambiguous names require 'name from source <source ID>'.");
                await Open(selection.Source, selection.Snapshot, origin, eligible, command.ToolCapable);
                return;
            }
            if (string.Equals(command.Action, "Close", StringComparison.Ordinal)) { await CloseAndWait(); return; }
            if (command.ToolCapable && command.Action is "McpCancelAnswer" && window is not null)
            { RetireMcpAnswer(); await JoinMcpAnswer(); return; }
            if (!Eligible())
            {
                RetireMcpAnswer();
                await JoinMcpAnswer();
                throw new InvalidOperationException("The exact result owner admission was retired.");
            }
            if (window is null || !active.IsCompleted)
            { throw new InvalidOperationException("Open a current exact-source review and wait for its owned operation first."); }
            if (command.ToolCapable != toolMode)
            { throw new InvalidOperationException("Instruction-only and tool-capable invocation commands are separate."); }
            if (toolMode && command.Action is "McpQuestion")
            { SetMcpQuestion(command.Argument, origin); return; }
            if (string.Equals(command.Action, "Request", StringComparison.Ordinal))
            {
                window.Request.Text = command.Argument;
                requestOrigin = origin;
                RetireConfirmation();
                return;
            }
            if (toolMode && command.Action is "Address" or "SecondAddress")
            {
                if (command.Action is "Address") { window.Address.Text = command.Argument; }
                else { window.SecondAddress.Text = command.Argument; }
                requestOrigin = origin;
                RetireConfirmation();
                return;
            }
            if (toolMode && command.Action is "McpServer" or "McpAccount" or "McpArguments")
            {
                if (toolSnapshot?.McpReference is null)
                { throw new InvalidOperationException("Open a skill with the exact declarative MCP reference first."); }
                if (command.Action is "McpArguments") { window.McpArguments.Text = command.Argument; }
                else if (command.Action is "McpServer")
                {
                    window.McpServers.SelectedItem = (window.McpServers.ItemsSource as IReadOnlyList<Kora.Application.Configuration.McpServerConfigurationSummary>)?
                        .SingleOrDefault(item => string.Equals(item.Id, command.Argument, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException("Refresh MCP options and independently select the exact included server ID.");
                }
                else
                {
                    var matches = (window.McpAccounts.ItemsSource as IReadOnlyList<Kora.Core.Mcp.McpAccountChoice>)?
                        .Where(item => string.Equals(item.DisplayName, command.Argument, StringComparison.Ordinal)).ToArray();
                    window.McpAccounts.SelectedItem = matches is { Length: 1 } ? matches[0]
                        : throw new InvalidOperationException("Select one current delegated account natively, or use its unique exact displayed name.");
                }
                requestOrigin = origin;
                RetireConfirmation();
                return;
            }
            if (string.Equals(command.Action, "Target", StringComparison.Ordinal))
            {
                if (!Guid.TryParseExact(command.Argument, "D", out var id))
                { throw new InvalidOperationException("Use the exact existing Active conversation ID from the displayed metadata page."); }
                var matches = (window.Targets.ItemsSource as SharedSkillConversationTarget[])?
                    .Where(item => item.Entry.Authority.SessionId.Value == id).ToArray();
                if (matches is not { Length: 1 }) { throw new InvalidOperationException("Refresh the metadata page and select exactly one displayed Active conversation."); }
                window.Targets.SelectedItem = matches[0];
                return;
            }
            active = RunReported(command.Action, origin, eligible);
            await active;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException or OperationCanceledException)
        {
            UseFailed(logger, exception.GetType().Name);
            main.ReportHostInteractionFailure("Shared skill use unavailable or cancelled. No replay or successful receipt claimed. Refresh and review again.");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native skill buttons require a void callback; RunReported logs failures and retires the exact review.")]
    private async void Start(string action, RequestOrigin origin)
    {
        if (action is "Close")
        {
            try { await CloseAndWait(); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                UseFailed(logger, exception.GetType().Name);
                main.ReportHostInteractionFailure("Reviewed skill shutdown is unconfirmed; no resource release or successful receipt claimed.");
            }
            return;
        }
        if (action is "McpCancelAnswer")
        {
            try { RetireMcpAnswer(); await JoinMcpAnswer(); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                UseFailed(logger, exception.GetType().Name);
                main.ReportHostInteractionFailure("MCP result answer cancellation is unconfirmed; no retry or resource release claimed.");
            }
            return;
        }
        if (!active.IsCompleted) { return; }
        active = RunReported(action, origin, Eligible);
        await active;
    }

    private async Task RunReported(string action, RequestOrigin origin, Func<bool> eligible)
    {
        var capturedLifetime = lifetime;
        try { await Run(action, origin, eligible); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            UseFailed(logger, exception.GetType().Name);
            review?.Dispose();
            referenceConsent?.Dispose();
            toolReview?.Dispose();
            RetireMcpAnswer();
            try { await JoinMcpAnswer(); }
            catch (Exception joinFailure) when (joinFailure is not OutOfMemoryException)
            { UseFailed(logger, joinFailure.GetType().Name); }
            if (window is { } opened)
            { opened.Clear(); opened.Status.Text = "Unavailable, cancelled or unconfirmed. Inspect the target conversation before any fresh review; no automatic retry."; }
        }
        finally
        {
            if (window is null && ReferenceEquals(lifetime, capturedLifetime))
            { lifetime?.Dispose(); lifetime = null; }
        }
    }

    private async Task Run(string action, RequestOrigin origin, Func<bool> eligible)
    {
        if (toolMode) { await RunTool(action, origin, eligible); return; }
        var opened = window ?? throw new InvalidOperationException("Review window is closed.");
        if (referenceConsent is { } declaration)
        {
            if (!string.Equals(action, "Prepare", StringComparison.Ordinal))
            { throw new InvalidOperationException("First separately authorize the displayed exact declaration's local reads. No enablement is granted."); }
            bool DeclaredCurrent() => Eligible() && eligible() && ReferenceEquals(window, opened);
            opened.ReadReferences.IsEnabled = false;
            var capturedReview = await workflow.ConsentToReferencesAsync(declaration, DeclaredCurrent, origin, lifetime!.Token);
            if (!DeclaredCurrent()) { capturedReview.Dispose(); return; }
            review = capturedReview;
            referenceConsent = null;
            opened.Instructions.Text = capturedReview.Text;
            opened.SetReferenceConsent(false);
            opened.Status.Text = "Complete primary and declared reference contents captured. Review ALL content, then separately enable. Nothing invoked.";
            return;
        }
        var captured = review ?? throw new InvalidOperationException("Exact skill review is unavailable.");
        var token = lifetime!.Token;
        bool Current() => Eligible() && eligible() && ReferenceEquals(window, opened) && captured.IsCurrent;
        if (!Current()) { throw new InvalidOperationException("Review authority expired."); }
        if (string.Equals(action, "Enable", StringComparison.Ordinal))
        {
            await workflow.EnableAsync(captured, Current, origin, token);
            if (Current()) { opened.Status.Text = "Exact revision enabled for one local-only use until this review expires. Explicitly choose a target and original request."; }
        }
        else if (action is "Targets" or "NextTargets")
        {
            var after = string.Equals(action, "NextTargets", StringComparison.Ordinal) ? next ?? throw new InvalidOperationException("No next page; refresh targets.") : (Guid?)null;
            RetireConfirmation();
            opened.Targets.ItemsSource = null;
            var page = await workflow.ReadTargetsAsync(after, Current, origin, token);
            if (!Current()) { return; }
            next = page.Next;
            opened.Targets.ItemsSource = page.Records.Where(item => item.Authority.IsActive).Select(item => new SharedSkillConversationTarget(item)).ToArray();
            opened.Status.Text = "Explicitly select an existing Active conversation. No session was created or resumed.";
        }
        else if (string.Equals(action, "Prepare", StringComparison.Ordinal))
        {
            RetireConfirmation();
            var selection = (opened.Targets.SelectedItem as SharedSkillConversationTarget)?.Entry
                ?? throw new InvalidOperationException("Select exactly one existing Active conversation.");
            var revision = inputRevision;
            bool Exact() => Current() && inputRevision == revision;
            using var request = HostActivity.BeginRoot(HostRequest.Create(requestOrigin), HostActivityLayer.Desktop, HostOperation.Request);
            invocation = await workflow.PrepareAsync(captured, selection, opened.Request.Text ?? string.Empty, Exact, requestOrigin, token);
            request.Complete(HostOperationOutcome.Completed);
            if (!Exact()) { invocation.Dispose(); invocation = null; return; }
            opened.Confirmation.Text = $"{opened.Instructions.Text}\n\nTARGET {selection.Authority.SessionId.Value:D}\nORIGINAL REQUEST\n{invocation.Body}";
            opened.Confirm.IsEnabled = true;
            opened.Status.Text = "Confirm this exact skill, original request and target. Only a retained answer or truthful terminal failure completes the invocation.";
        }
        else if (string.Equals(action, "Invoke", StringComparison.Ordinal))
        {
            var prepared = invocation ?? throw new InvalidOperationException("Review the exact invocation before confirming.");
            invocation = null;
            opened.Confirm.IsEnabled = false;
            var revision = inputRevision;
            var receipt = await workflow.InvokeAsync(prepared, () => Current() && inputRevision == revision, token);
            if (Eligible() && ReferenceEquals(window, opened))
            {
                opened.Confirmation.Text = string.Empty;
                opened.Status.Text = $"Retained exact request {receipt.Message.Request.RequestId.Value:D}: {receipt.Message.Outcome} / {receipt.Message.Reason}. "
                    + (receipt.Message.AssistantResponse?.Body ?? "No assistant answer. Inspect the exact conversation; no automatic replay.");
            }
        }
        else { throw new InvalidOperationException("Use an exact shared skill review, enable, target, request, review invocation, confirm invocation or close command."); }
    }

    private void RetireConfirmation()
    {
        RetireMcpAnswer();
        Interlocked.Increment(ref inputRevision);
        if (invocation is not null) { invocation.Dispose(); invocation = null; }
        toolInvocation?.Dispose();
        toolInvocation = null;
        mcpInvocation?.Dispose();
        mcpInvocation = null;
        if (window is not null) { window.McpResult.Text = string.Empty; }
        if (window is not null) { window.Confirm.IsEnabled = false; window.Confirmation.Text = string.Empty; }
    }

    private void InputChanged()
    {
        Interlocked.Increment(ref toolInputRevision);
        RetireConfirmation();
        if (toolMode) { RetireToolReview(); }
        requestOrigin = RequestOrigin.LocalUi;
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) => Close();
    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    { if (!Eligible()) { Close(); } }
    private void Close()
    {
        if (showing)
        {
            closeAfterShow = true;
            lifetime?.Cancel();
            review?.Dispose();
            referenceConsent?.Dispose();
            toolReview?.Dispose();
            window?.Clear();
            return;
        }
        var opened = window;
        RetireMcpAnswer();
        window = null;
        lifetime?.Cancel();
        review?.Dispose();
        review = null;
        referenceConsent?.Dispose();
        referenceConsent = null;
        RetireToolReview();
        toolSource = null;
        toolSnapshot = null;
        toolMode = false;
        invocation?.Dispose();
        invocation = null;
        opened?.Clear();
        opened?.Close();
    }
    private async Task CloseAndWait()
    {
        Close();
#pragma warning disable VSTHRD003 // Join this controller's owned submission before disposing its cancellation source.
        await active;
#pragma warning restore VSTHRD003
        await JoinMcpAnswer();
        lifetime?.Dispose();
        lifetime = null;
    }
    public void Dispose()
    {
        disposed = true;
        Close();
        if (active.IsCompleted) { lifetime?.Dispose(); lifetime = null; }
        main.SharedSkillInvocationRequested -= Route;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.PropertyChanged -= OnMainChanged;
        main.BindSharedSkillUseShutdown(null);
        sources.BindToolInvocation(null);
    }
}
