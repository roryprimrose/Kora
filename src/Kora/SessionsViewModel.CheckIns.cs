using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    // NumericUpDown bounds are decimal; these mirror the single integer authority in OrdinaryTurnDeadline.
    public static readonly decimal MinimumCheckInMinutes = OrdinaryTurnDeadline.MinimumMinutes;
    public static readonly decimal MaximumCheckInMinutes = OrdinaryTurnDeadline.MaximumMinutes;

    private bool answeringCheckIn;
    private decimal checkInMinutes = OrdinaryTurnDeadline.DefaultMinutes;

    private ConversationCheckInTarget? CurrentCheckIn =>
        !closed && access.CanInspect && access.CanControl && !answeringCheckIn
        && runningConversation is { Phase: ConversationExecutionPhase.Running, CheckIn: { } checkIn } observed
        && observed.Target.Request.SessionId == selected?.Authority.SessionId
        && observed.Target.Generation == selected.Authority.Generation
        && conversation?.IsExecutionProgressCurrent(observed) == true
            ? new(observed.Target, checkIn) : null;

    public bool HasTurnCheckIn => CurrentCheckIn is not null;

    public string TurnCheckInText => CurrentCheckIn is { } target
        ? "Still working after " + Minutes((int)target.CheckIn.ActiveElapsed.TotalMinutes)
            + " of active time. The turn keeps running while you decide; nothing is paused. "
            + "Choose how long to keep going, or stop it. You can also say \"keep going\", \"keep going for 10 minutes\", "
            + "\"keep going until done\" or \"stop this turn\"."
        : string.Empty;

    public string KeepGoingCheckInLabel => CurrentCheckIn is { } target
        ? "Keep going for another " + Minutes(target.CheckIn.Interval.Minutes) : "Keep going";

    public decimal CheckInMinutes
    {
        get => checkInMinutes;
        set => SetProperty(ref checkInMinutes, value);
    }

    public Task KeepGoingCheckInAsync() => AnswerCheckInAsync(new(ConversationCheckInOperation.KeepGoing));

    public Task KeepGoingForCheckInAsync()
    {
        if (checkInMinutes != decimal.Truncate(checkInMinutes)
            || checkInMinutes is < OrdinaryTurnDeadline.MinimumMinutes or > OrdinaryTurnDeadline.MaximumMinutes)
        {
            runningConversationStatus = "Choose whole minutes from 1 through 60. The running turn continues unchanged.";
            NotifyConversationCancellation();
            return Task.CompletedTask;
        }
        return AnswerCheckInAsync(new(ConversationCheckInOperation.KeepGoingFor, new OrdinaryTurnDeadline((int)checkInMinutes)));
    }

    public Task KeepRunningCheckInAsync() => AnswerCheckInAsync(new(ConversationCheckInOperation.KeepRunning));

    public Task StopCheckInAsync() => AnswerCheckInAsync(new(ConversationCheckInOperation.Stop));

    private async Task AnswerCheckInAsync(ConversationCheckInChoice choice)
    {
        if (CurrentCheckIn is not { } target) { return; }
        var session = RequireSelected();
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        bool Live() => !closed && access.CanInspect && access.CanControl && access.ControlRevision == control
            && selectionEpoch == epoch && selected == session;
        runningInspectionEpoch++;
        answeringCheckIn = true;
        NotifyConversationCancellation();
        try
        {
            var answered = await conversation!.AnswerCheckInAsync(target, choice, Live, lifetime.Token);
            if (Live()) { PresentConversationExecution(answered); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (Live())
            {
                runningConversationStatus = "Check-in answer unconfirmed: " + exception.Message
                    + " The running turn continues unless Stop was accepted; inspect this exact task.";
            }
        }
        finally
        {
            answeringCheckIn = false;
            NotifyConversationCancellation();
        }
    }

    private void NotifyTurnCheckIn()
    {
        OnPropertyChanged(nameof(HasTurnCheckIn));
        OnPropertyChanged(nameof(TurnCheckInText));
        OnPropertyChanged(nameof(KeepGoingCheckInLabel));
    }

    private static string Minutes(int value) =>
        value.ToString(CultureInfo.InvariantCulture) + (value == 1 ? " minute" : " minutes");
}
