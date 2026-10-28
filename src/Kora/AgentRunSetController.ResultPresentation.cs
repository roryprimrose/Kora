using Kora.Application.Agents;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AgentRunSetController
{
    private sealed record ReviewedPresentation(AgentRunSet Runs, AgentRunSetWindow.RunPanel Panel,
        long Revision, SessionWorkspaceEntry Target, BoundedAgentReview Review);

    private readonly ReviewedPresentation?[] presentations = new ReviewedPresentation?[2];

    private AgentRunSet.AgentRunSnapshot? ReadCurrentPresentation(int number, AgentRunSetWindow.RunPanel panel)
    {
        var presentation = presentations[number - 1];
        if (!Eligible() || !ReferenceEquals(window?.Runs[number - 1], panel)
            || presentation is null || !ReferenceEquals(presentation.Runs, runs)
            || !ReferenceEquals(presentation.Panel, panel) || presentation.Revision != panel.Revision
            || panel.Targets.SelectedItem is not AgentRunSetWindow.Target selected || selected.Entry != presentation.Target)
        { return null; }
        var snapshot = presentation.Runs.Read(number);
        return snapshot is not null && snapshot.Target == presentation.Target && ReferenceEquals(snapshot.Review, presentation.Review)
            ? snapshot : null;
    }

    private void RequireCurrentPresentation(int number, AgentRunSetWindow.RunPanel panel)
    {
        if (ReadCurrentPresentation(number, panel) is null)
        { throw new InvalidOperationException("Review the exact current source, task and target first."); }
    }

    private void ShowStatus(int number, AgentRunSetWindow.RunPanel panel)
    {
        var snapshot = ReadCurrentPresentation(number, panel);
        if (snapshot is null)
        {
            panel.ClearReview();
            panel.Status.Text = $"Run {number}: Review retired or unavailable. Review the exact source, task and target again. "
                + "Any retained result remains only in its original conversation; no cancellation or rollback claimed.";
            return;
        }
        panel.Status.Text = snapshot.Receipt is { } receipt
            ? $"Run {number}: {receipt.Message.Outcome}; {receipt.Message.Reason}. Result belongs only to the selected conversation."
            : $"Run {number}: {snapshot.Status}. Allocated or invoking does not establish provider execution.";
        if (ReadCurrentPresentation(number, panel) is { } current)
        { panel.Result.Text = current.Receipt?.Message.AssistantResponse?.Body ?? string.Empty; }
    }
}
