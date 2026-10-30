using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Context;
using Kora.Application.Hosting;
using Kora.Core.Context;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Tools.Clipboard;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class QuestionClipboardAnswer : IDisposable
{
    private readonly NativeQuestionViewModel question;
    private readonly ClipboardSnapshotBroker broker;
    private readonly ClipboardRead read;
    private readonly NativeQuestionHost permissionHost;
    private readonly NativeDetailRenderer renderer;
    private readonly Func<Func<bool>> captureAdmission;
    private readonly TimeProvider time;
    private readonly ILogger<NativeQuestionViewModel> logger;
    private Window? owner;
    private QuestionWindow? permissionWindow;
    private CancellationTokenSource? cancellation;
    private Task run = Task.CompletedTask;
    private TaskCompletionSource? ready;
    private TaskCompletionSource<ClipboardOutcome>? release;
    private Func<CancellationToken, Task>? validatePermission;
    private Func<bool> original = static () => false;
    private ClipboardSnapshot? snapshot;
    private HostQuestionRecord? target;
    private QuestionAnswer? previous;
    private QuestionAnswer? accepted;
    private ActivityLink[] links = [];
    private string reviewText = string.Empty;
    private bool displayed;
    private bool replacing;
    private volatile bool disposed;
    private bool validating;
    private long revision;
    private int visible;

    internal QuestionClipboardAnswer(NativeQuestionViewModel question, IPlainTextClipboardReader reader,
        ClipboardExplanationAdmission admission, WindowsSqliteHostInteractionStore store,
        Func<Func<bool>> captureAdmission, Func<Kora.Application.Communication.CallPolicyObservation> call,
        TimeProvider time, ILogger<ClipboardSnapshotBroker> brokerLogger,
        ILogger<NativeQuestionViewModel> logger, NativeDetailRenderer renderer)
    {
        this.question = question;
        this.captureAdmission = captureAdmission;
        this.time = time;
        this.logger = logger;
        this.renderer = renderer;
        broker = new(reader, time, brokerLogger);
        permissionHost = new(store, time, logger);
        permissionHost.BindGate(() => original());
        permissionHost.BindCallObservation(call);
        var permissions = new ClipboardPermissionSourceUsePolicy(store, store, time);
        permissions.BindPresenter(async (proposal, generation, current, token) =>
            await Dispatcher.UIThread.InvokeAsync(() => permissionHost.RequestClipboardPermissionAsync(
                proposal, generation, current, model =>
                {
                    if (owner is not { IsVisible: true } || !original())
                    { throw new OperationCanceledException(token); }
                    permissionWindow = new(model, renderer);
                    permissionWindow.Show(owner);
                    permissionWindow.Activate();
                    return Task.CompletedTask;
                }, token)));
        read = new(new ClipboardReadWorkflow(broker, admission, permissions, ReviewAsync));
    }

    internal static bool Supports(HostQuestionRecord record) =>
        record is { Proposal: null, Status: QuestionStatus.Pending, Spec.Kind: QuestionKind.Text }
        && record.Key.Request.Origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
        && string.Equals(record.Spec.Purpose, LocalModelQuestionPolicy.Purpose, StringComparison.Ordinal)
        && string.Equals(record.Spec.SourceId, LocalModelQuestionPolicy.SourceId, StringComparison.Ordinal);

    internal void BindWindow(Window window)
    {
        if (owner is not null) { throw new InvalidOperationException("Only one exact clipboard-answer presenter is permitted."); }
        owner = window;
        window.PropertyChanged += OnWindowChanged;
    }

    private void OnWindowChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != Avalonia.Visual.IsVisibleProperty) { return; }
        Volatile.Write(ref visible, owner?.IsVisible == true ? 1 : 0);
        if (Volatile.Read(ref visible) == 0) { Clear("Clipboard answer presentation closed. Nothing was submitted."); }
    }

#pragma warning disable VSTHRD003 // Observes this owner's already-started read/review and physical reader join.
    internal Task Quiescence => run;
#pragma warning restore VSTHRD003
    internal bool IsActive => cancellation is not null;
    internal bool BlocksSubmission => IsActive && accepted is null || validating;
    internal bool HasAccepted => accepted is not null;
    internal string ReviewText => snapshot is not null && Current() ? reviewText : string.Empty;
    internal bool NeedsReplacement => previous?.Text is { Length: > 0 };
    internal bool CanCapture => !disposed && !IsActive && owner is { IsVisible: true, IsActive: true }
        && question.IsEditable && Supports(question.Question);
    internal bool CanAccept => !validating && accepted is null && displayed && Current()
        && owner is { IsActive: true } && (!NeedsReplacement || replacing);
    internal bool CanCommit => accepted is not null && ReferenceEquals(question.Answer, accepted)
        && !validating && Current();

    internal void AcknowledgeReplacement(bool value) { replacing = value; question.ClipboardChanged(); }
    internal void CompletePresentation(string text)
    {
        displayed = reviewText.Length != 0 && string.Equals(text, reviewText, StringComparison.Ordinal) && Current();
    }

    internal async Task CaptureAsync()
    {
        if (!CanCapture)
        {
            question.ReportOutcome("Clipboard answer unavailable: use one current ordinary text clarification in its original native window.");
            return;
        }
        question.StopVoiceReply();
        using var native = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Presentation);
        var admission = captureAdmission();
        var version = Interlocked.Increment(ref revision);
        var exact = question.Question;
        target = exact;
        previous = question.Answer;
        original = () => !disposed && Interlocked.Read(ref revision) == version && admission()
            && Volatile.Read(ref visible) == 1 && question.IsClipboardTargetLive(exact) && Supports(exact);
        if (!original()) { Clear("Clipboard answer denied before reading. The manual draft is unchanged."); return; }
        cancellation = new();
        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ready = presented;
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        run = RunAsync(cancellation.Token);
        question.ClipboardChanged();
        await presented.Task;
        native.Complete(HostOperationOutcome.Completed);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                if (!await question.ValidateClipboardTargetAsync(target!))
                { throw new InvalidOperationException("The exact clipboard-answer question is no longer current."); }
                var outcome = await read.ExecuteAsync(original, token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (snapshot is null && !token.IsCancellationRequested)
                    { question.ReportOutcome("Clipboard answer: " + outcome + ". No answer was submitted; the manual draft is unchanged."); }
                });
            }, HostActivityLayer.Desktop, HostOperation.Presentation);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                NativeQuestionViewModel.ClipboardFailure(logger, exception.GetType().FullName);
                Clear("Clipboard answer failed or permission became unavailable. Nothing was submitted; the manual draft is unchanged.");
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                permissionWindow?.Close();
                permissionWindow = null;
                ready!.TrySetResult();
                cancellation?.Dispose();
                cancellation = null;
                broker.Clear();
                snapshot = null;
                validatePermission = null;
                reviewText = string.Empty;
                question.ClipboardChanged();
            });
            await broker.WaitForQuiescenceAsync();
        }
    }

    private async Task<ClipboardOutcome> ReviewAsync(ClipboardSnapshot capture,
        Func<CancellationToken, Task> validate, CancellationToken token)
    {
        var context = Activity.Current;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            permissionWindow?.Close();
            permissionWindow = null;
            if (!original() || !await question.ValidateClipboardTargetAsync(target!))
            { release!.TrySetResult(ClipboardOutcome.Stale); return; }
            ConversationText.Validate(capture.Text, submission: true);
            if (!target!.Spec.Accepts(new([], capture.Text), submitting: true))
            { throw new InvalidDataException("The whole clipboard text exceeds this question's answer bounds."); }
            snapshot = capture;
            validatePermission = validate;
            links = context is null ? [] : [new(context.Context)];
            reviewText = $"Clipboard snapshot {capture.SnapshotId:D}\nFormat: {ClipboardSnapshot.Format}; native version: {capture.Version}; "
                + $"captured: {capture.CapturedAt:O}; UTF-8 bytes: {capture.Utf8Bytes}.\n"
                + "Complete captured text (unsubmitted, not authority):\n" + capture.Text
                + (NeedsReplacement ? "\n\nCurrent manual draft (unchanged):\n" + previous!.Text : string.Empty);
            question.ReportOutcome("Review the complete captured text. Accept as draft does not submit, approve an action or authorize a model transmission.");
            question.ClipboardChanged();
            ready!.TrySetResult();
        });
        return await release!.Task.WaitAsync(token).ConfigureAwait(false);
    }

    internal async Task AcceptAsync()
    {
        if (!CanAccept) { return; }
        if (!await ValidateAsync()) { return; }
        if (!CanAccept) { Clear("Clipboard review changed. The manual draft is unchanged."); return; }
        accepted = new([], snapshot!.Text);
        question.SetClipboardDraft(accepted);
        question.ReportOutcome("Reviewed clipboard text is an unsubmitted answer draft. Separately choose Submit answer; normal task and transmission admission still apply.");
    }

    internal async Task<bool> ValidateSubmitAsync()
    {
        if (validating) { return false; }
        if (!HasAccepted) { return !IsActive; }
        return await ValidateAsync() && CanCommit;
    }

    private async Task<bool> ValidateAsync()
    {
        if (!Current()) { Clear("Clipboard source or question changed. Nothing was submitted; the prior manual draft is preserved."); return false; }
        validating = true;
        question.ClipboardChanged();
        try
        {
            using var activity = HostActivity.BeginRoot(snapshot!.Request, HostActivityLayer.Desktop, HostOperation.Presentation, links);
            await validatePermission!(cancellation!.Token);
            var live = Current() && await question.ValidateClipboardTargetAsync(target!);
            activity.Complete(live ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            if (!live) { Clear("Clipboard question or permission is stale. Nothing was submitted; the prior manual draft is preserved."); }
            return live;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            NativeQuestionViewModel.ClipboardFailure(logger, exception.GetType().FullName);
            Clear("Clipboard permission is no longer available. Nothing was submitted; the prior manual draft is preserved.");
            return false;
        }
        finally { validating = false; question.ClipboardChanged(); }
    }

    private bool Current() => snapshot is not null && original()
        && time.GetUtcNow() >= snapshot.CapturedAt
        && time.GetUtcNow() < snapshot.CapturedAt + ClipboardExplanationContext.Lifetime
        && ReferenceEquals(broker.Current, snapshot) && !cancellation!.IsCancellationRequested;

    internal void Refresh()
    {
        if (IsActive && (!original() || snapshot is not null && !Current()))
        { Clear("Clipboard answer retired because the original question, source or private input changed. Nothing was submitted."); }
    }

    internal void ManualEdit()
    {
        if (IsActive) { Clear("Clipboard review cancelled by manual editing. The current manual draft is unchanged.", restore: false); }
    }

    internal void Submitted()
    {
        accepted = null;
        previous = null;
        release?.TrySetResult(ClipboardOutcome.Captured);
    }

    internal void Clear(string status = "Clipboard answer cancelled. The prior manual draft is unchanged.", bool restore = true)
    {
        Interlocked.Increment(ref revision);
        cancellation?.Cancel();
        permissionWindow?.Close();
        permissionWindow = null;
        if (restore && accepted is not null && previous is not null)
        { question.RestoreClipboardDraft(accepted, previous); }
        accepted = null;
        previous = null;
        snapshot = null;
        validatePermission = null;
        reviewText = string.Empty;
        displayed = false;
        replacing = false;
        broker.Clear();
        release?.TrySetResult(ClipboardOutcome.Cancelled);
        question.ReportOutcome(status);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        if (owner is not null) { owner.PropertyChanged -= OnWindowChanged; }
        Clear();
        permissionHost.Dispose();
        broker.Dispose();
    }
}
