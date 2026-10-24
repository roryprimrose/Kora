namespace Kora.Core.Dependencies;

public sealed class ModelFinalResponse : ModelAssistantMessage
{
    internal ModelFinalResponse(string answer) => Answer = answer;
    public string Answer { get; }
}
