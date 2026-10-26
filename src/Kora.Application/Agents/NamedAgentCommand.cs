using Kora.Core.Conversation;

namespace Kora.Application.Agents;

public sealed record NamedAgentCommand(string Name, string Input)
{
    public static NamedAgentCommand Parse(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (!argument.StartsWith('"'))
        { throw new InvalidDataException("Use agent invoke \"Exact readable name\" followed by a space and the original task."); }
        var closing = argument.IndexOf('"', 1);
        if (closing is < 2 or > 129 || closing + 1 >= argument.Length || argument[closing + 1] != ' ')
        { throw new InvalidDataException("Select one quoted readable agent name and supply explicit input."); }
        var name = argument[1..closing];
        if (name.Any(char.IsControl) || string.IsNullOrWhiteSpace(name))
        { throw new InvalidDataException("The exact agent name must be readable bounded text."); }
        var input = argument[(closing + 2)..];
        ConversationText.Validate(input, submission: true);
        return new(name, input);
    }
}
