namespace Kora.Core.Interaction;

public sealed record QuestionOption
{
    public QuestionOption(string id, string label)
    {
        Id = InteractionValidation.Identifier(id);
        Label = InteractionValidation.Text(label, 512);
    }

    public string Id { get; }
    public string Label { get; }
}
