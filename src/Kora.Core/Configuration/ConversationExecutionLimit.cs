namespace Kora.Core.Configuration;

/// <summary>A desired future ordinary-conversation ceiling, never runtime qualification.</summary>
public sealed record ConversationExecutionLimit
{
    public const int Maximum = 2;
    public static ConversationExecutionLimit Default { get; } = new(Maximum);

    public ConversationExecutionLimit(int maximum)
    {
        if (maximum is < 1 or > Maximum) { throw new ArgumentOutOfRangeException(nameof(maximum)); }
        Value = maximum;
    }

    public int Value { get; }
    public string Serialize() => Value == 1 ? "1" : "2";

    public static ConversationExecutionLimit Parse(string value) => value switch
    {
        "1" => new(1),
        "2" => new(2),
        _ => throw new InvalidDataException("Conversation execution maximum must be the canonical integer 1 or 2."),
    };
}
