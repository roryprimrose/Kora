namespace Kora.Application.ViewModels;

public sealed record ModelQuestionChoice(int Number, string Text)
{
    public string DisplayText => $"{Number}. {Text}";
}
