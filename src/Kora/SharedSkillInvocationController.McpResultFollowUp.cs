using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SharedSkillInvocationController
{
    private readonly McpResultCapture skillMcpCapture = new();
    private McpResultObservation? skillMcpObservation;
    private McpResultFollowUpWorkflow? skillMcpAnswer;
    private McpResultFollowUpReview? skillMcpAnswerReview;
    private RequestOrigin skillMcpQuestionOrigin;
    private long skillMcpQuestionRevision;
    private bool settingMcpQuestion;
    private Task skillMcpRetirement = Task.CompletedTask;

    private void SetMcpQuestion(string question, RequestOrigin origin)
    {
        if (skillMcpObservation is null || skillMcpAnswer?.Held == true || skillMcpAnswer?.IsRunning == true)
        { throw new InvalidOperationException("Only the current successful MCP skill result admits a fresh question; held work cannot be retried."); }
        settingMcpQuestion = true;
        try { window!.McpQuestion.Text = question; }
        finally { settingMcpQuestion = false; }
        skillMcpQuestionOrigin = origin;
        RetireMcpAnswerReview();
    }

    private void McpQuestionChanged()
    {
        if (settingMcpQuestion) { return; }
        if (skillMcpAnswer?.IsRunning == true) { RetireMcpAnswer(); return; }
        skillMcpQuestionOrigin = RequestOrigin.LocalUi;
        RetireMcpAnswerReview();
    }

    private void RetireMcpAnswerReview()
    {
        skillMcpQuestionRevision++;
        skillMcpAnswerReview?.Dispose();
        skillMcpAnswerReview = null;
        if (window is { } opened)
        {
            opened.McpAnswerReview.Text = string.Empty;
            opened.McpAnswerOutcome.Text = string.Empty;
            opened.McpConfirmAnswer.IsEnabled = false;
        }
    }

    private async Task RunMcpAnswer(string action, RequestOrigin origin, Func<bool> eligible)
    {
        var opened = window ?? throw new InvalidOperationException("The original skill result owner is closed.");
        if (action is "McpCancelAnswer") { RetireMcpAnswer(); await JoinMcpAnswer(); return; }
        var source = skillMcpObservation ?? throw new InvalidOperationException("No current successful joined MCP skill result is available.");
        var revision = skillMcpQuestionRevision;
        bool Current() => Eligible() && eligible() && ReferenceEquals(window, opened)
            && ReferenceEquals(skillMcpObservation, source) && revision == skillMcpQuestionRevision
            && skillMcpQuestionOrigin == origin;
        if (!Current()) { RetireMcpAnswer(); throw new InvalidOperationException("The fresh question's original channel or source was retired."); }
        using var operation = HostActivity.BeginRoot(new(new(Guid.NewGuid()), source.Request.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Desktop, HostOperation.Request, [source.Cause]);
        var answerWorkflow = skillMcpAnswer ??= toolWorkflow!.CreateMcpResultFollowUpWorkflow();
        var token = lifetime!.Token;
        opened.McpQuestion.IsEnabled = false;
        opened.McpConfirmAnswer.IsEnabled = false;
        try
        {
            if (action is "McpReviewAnswer")
            {
                skillMcpAnswerReview?.Dispose();
                skillMcpAnswerReview = await answerWorkflow.ReviewAsync(source, opened.McpQuestion.Text ?? string.Empty, Current, token, origin);
                if (!Current()) { RetireMcpAnswer(); return; }
                opened.McpAnswerReview.Text = skillMcpAnswerReview.Text;
                opened.McpConfirmAnswer.IsEnabled = skillMcpAnswerReview.CanConfirm;
                opened.McpAnswerOutcome.Text = skillMcpAnswerReview.Decision.Kind switch
                {
                    McpResultSourceUseKind.QualifiedLocalOnly => "Review the complete result, provenance, unchanged new question, channels and retention. Separately confirm ONE local-only no-tools answer.",
                    McpResultSourceUseKind.Denied => "Denied by source-use screening; no override, model call or source refresh.",
                    _ => "Unavailable: complete source-use screening is unknown. Nothing submitted.",
                };
                operation.Complete(HostOperationOutcome.Completed);
            }
            else if (action is "McpConfirmAnswer")
            {
                var confirmedReview = skillMcpAnswerReview ?? throw new InvalidOperationException("Separately review this complete result and unchanged new question first.");
                skillMcpAnswerReview = null;
                var receipt = await answerWorkflow.ConfirmAsync(confirmedReview, Current, token);
                operation.Complete(receipt.Message.AssistantResponse is null ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
                if (!Current()) { RetireMcpAnswer(); return; }
                opened.McpAnswerReview.Text = string.Empty;
                opened.McpAnswerOutcome.Text = receipt.Message.AssistantResponse is { } answer ? "ASSISTANT\n" + answer.Body
                    : $"HOST {receipt.Message.Outcome}/{receipt.Message.Reason}\nNo assistant answer was retained.";
            }
            else { throw new InvalidOperationException("Use the exact MCP result question/review/confirm/cancel commands."); }
        }
        finally
        {
            opened.McpQuestion.IsEnabled = Current() && !answerWorkflow.Held && !answerWorkflow.IsRunning;
        }
    }

    private void RetireMcpAnswer()
    {
        skillMcpObservation = null;
        skillMcpCapture.Retire();
        RetireMcpAnswerReview();
        if (window is { } opened)
        {
            opened.McpQuestion.Text = string.Empty;
            opened.McpQuestion.IsEnabled = false;
            opened.McpResult.Text = string.Empty;
        }
        if (skillMcpAnswer is { } answerWorkflow) { skillMcpRetirement = answerWorkflow.CancelAsync(); }
    }

    private async Task JoinMcpAnswer()
    {
#pragma warning disable VSTHRD003 // Joins this exact owner's retired source/provider work before any new invocation.
        await skillMcpRetirement;
#pragma warning restore VSTHRD003
        if (skillMcpAnswer?.Held == true)
        { throw new InvalidOperationException("The prior MCP result answer is unconfirmed; ownership prohibits retry."); }
    }
}
