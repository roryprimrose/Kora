namespace Kora;

public sealed partial class ResponseWindow
{
    internal void ShowInlineQuestion(QuestionView view)
    {
        if (sessionWindowClosed || !viewModel.CanRevealPrivatePresentation || !IsVisible)
        { throw new InvalidOperationException("The original owning question workbench is not presented."); }
        if (InlineQuestions.Children.Contains(view)) { view.Activate(); return; }
        InlineQuestions.Children.Add(view);
        InlineQuestionViewport.IsVisible = true;
        view.Closed += OnInlineQuestionClosed;
        UpdateLayout();
    }

    private void OnInlineQuestionClosed(object? sender, EventArgs args)
    {
        if (sender is not QuestionView view) { return; }
        view.Closed -= OnInlineQuestionClosed;
        InlineQuestions.Children.Remove(view);
        InlineQuestionViewport.IsVisible = InlineQuestions.Children.Count != 0;
    }
}
