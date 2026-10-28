using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.SettingInspection;

internal sealed class SettingInspectionPresenter(SettingInspectionWorkflow workflow, Func<Func<bool>> captureAdmission,
    Func<Func<bool>> captureLifetime, TimeProvider time) : INotifyPropertyChanged
{
    private SettingInspectionSession? session;
    private Func<bool>? admitted;
    private CancellationTokenSource? cancellation;
    private Task active = Task.CompletedTask;
    private string question = string.Empty;
    private string status = "Explicitly inspect confirmed settings and review your question. Opening this window reads nothing.";
    private string answer = string.Empty;
    private long revision;
    private bool closed;
    private bool busy;
    private bool confirmed;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Question
    {
        get => question;
        set
        {
            if (string.Equals(question, value, StringComparison.Ordinal)) { return; }
            Retire();
            question = value;
            Notify();
        }
    }
    public string Status => status;
    public string Answer => answer;
    public string ReviewText => session?.Review.Text ?? string.Empty;
    public bool IsBusy => busy;
    public bool CanReview => !closed && !busy;
    public bool CanConfirm => !closed && !busy && !confirmed && session is not null;

    public Task ReviewAsync()
    {
        if (!CanReview) { throw new InvalidOperationException("The previous inspection still owns work."); }
        RequireOriginalInput(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
        Retire();
        return active = ReviewCoreAsync();
    }

    private async Task ReviewCoreAsync()
    {
        var version = revision;
        using var operation = new CancellationTokenSource();
        cancellation = operation;
        busy = true;
        Notify();
        using var root = HostActivity.BeginRoot(
            HostActivity.Current?.Request ?? HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            var host = captureAdmission();
            var lifetime = captureLifetime();
            bool Current() => !closed && revision == version && host() && lifetime();
            var reviewed = await workflow.ReviewAsync(question, Current, operation.Token);
            if (!Current() || operation.IsCancellationRequested)
            { throw new OperationCanceledException("The native source review was retired."); }
            admitted = Current;
            session = reviewed;
            status = "Review the FULL source, provenance, exact question and NEW LOCAL target. Separately confirm once; no model has run.";
            root.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            status = "Cancelled. Source work has joined; nothing was submitted.";
            root.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            Retire();
            status = "Unavailable: saved/default state or original host admission is unconfirmed. "
                + "Repair the existing settings service and explicitly inspect again. Nothing sent. "
                + "Question maximum: 256 characters / 512 UTF-8 bytes.";
            root.Complete(HostOperationOutcome.Failed);
        }
        finally { cancellation = null; busy = false; Notify(); }
    }

    public Task ConfirmAsync()
    {
        if (!CanConfirm || session is not { } review || admitted is not { } current)
        { throw new InvalidOperationException("Review this exact original question and confirmed source first."); }
        RequireOriginalInput(review.Review.Original.Origin);
        return active = ConfirmCoreAsync(review, current);
    }

    private async Task ConfirmCoreAsync(SettingInspectionSession review, Func<bool> current)
    {
        var version = revision;
        using var operation = new CancellationTokenSource();
        cancellation = operation;
        busy = true;
        confirmed = true;
        status = "One local request in progress. Cancel revokes disclosure immediately; resource ownership continues until actual join.";
        Notify();
        using var root = HostActivity.BeginRoot(review.Review.Original, HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            bool Current() => version == revision && current();
            var result = await workflow.AnswerAsync(review, Current, operation.Token);
            if (!Current() || operation.IsCancellationRequested)
            { root.Complete(HostOperationOutcome.Cancelled); return; }
            answer = result.Outcome is SettingInspectionOutcome.Succeeded or SettingInspectionOutcome.Unsupported
                ? result.Receipt!.Message.AssistantResponse!.Body : string.Empty;
            status = $"Outcome: {result.Outcome}. "
                + (result.Receipt is null ? "No durable answer claimed. " : "Exact conversation/task receipt confirmed. ")
                + (result.Receipt is { } receipt ? $"Host result: {receipt.Message.Outcome}/{receipt.Message.Reason}. " : string.Empty)
                + "Review consumed. No automatic retry. Any setting change requires the separate suggestion Review/Confirm/Apply.";
            root.Complete(result.Outcome is SettingInspectionOutcome.Succeeded or SettingInspectionOutcome.Unsupported
                ? HostOperationOutcome.Completed : result.Outcome == SettingInspectionOutcome.Cancelled
                    ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Retire();
            status = "Unconfirmed: inspect Sessions without retry. No successful answer or rollback is claimed.";
            root.Complete(HostOperationOutcome.Failed);
        }
        finally { cancellation = null; busy = false; Notify(); }
    }

    public void RefreshAdmission()
    {
        if (admitted is not null && (!admitted() || session is { } review
            && (!review.Current() || time.GetUtcNow() >= review.Review.ExpiresAt)))
        {
            Retire();
            question = string.Empty;
            status = "Review retired: source, original input, privacy, call, owner or lifetime changed. No late answer admitted.";
            Notify();
        }
    }

    public async Task CancelAsync()
    {
        Retire();
        var version = revision;
        status = "Cancelled. Waiting for owned source/model work to physically join; no rollback or retry is inferred.";
        Notify();
#pragma warning disable VSTHRD003 // Joins only this presenter's owned source/model operation; cancellation is not completion.
        await active;
#pragma warning restore VSTHRD003
        if (revision == version)
        {
            status = "Cancelled. Owned source/model work has physically joined. No late answer, rollback or retry is inferred.";
            Notify();
        }
    }

    public async Task CloseAsync()
    {
        closed = true;
        question = string.Empty;
        await CancelAsync();
    }

    private void Retire()
    {
        revision++;
        session = null;
        admitted = null;
        confirmed = false;
        answer = string.Empty;
        cancellation?.Cancel();
    }

    private static void RequireOriginalInput(RequestOrigin origin)
    {
        SessionConversationService.RequireOriginalSource(origin);
        if (HostActivity.Current?.Activity?.IsStopped == true)
        { throw new InvalidOperationException("The original user input has ended."); }
    }

    private void Notify()
    {
        foreach (var name in new[] { nameof(Question), nameof(Status), nameof(Answer), nameof(ReviewText),
            nameof(IsBusy), nameof(CanReview), nameof(CanConfirm) })
        { OnPropertyChanged(name); }
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new(name));
}
