namespace Kora.Application.Presentation;

/// <summary>Captures only an existing host-owned presenter, never resolves or creates a question.</summary>
public interface IPendingQuestionNavigation
{
    IPendingQuestionFocus? CaptureFocus(Guid sessionId, Guid taskId);
}
