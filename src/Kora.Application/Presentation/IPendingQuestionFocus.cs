namespace Kora.Application.Presentation;

public interface IPendingQuestionFocus
{
    Task<bool> FocusAsync(Func<bool> isAdmitted);
}
