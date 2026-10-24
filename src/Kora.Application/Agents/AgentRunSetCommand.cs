namespace Kora.Application.Agents;

public sealed record AgentRunSetCommand(string Action, int Number = 0, string Argument = "")
{
    public static AgentRunSetCommand? Parse(string text)
    {
        if (string.Equals(text, "independent agents", StringComparison.OrdinalIgnoreCase)) { return new("Open"); }
        if (string.Equals(text, "independent agents close", StringComparison.OrdinalIgnoreCase)) { return new("Close"); }
        const string prefix = "agent run ";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return null; }
        var remainder = text[prefix.Length..];
        if (remainder.Length < 2 || remainder[1] != ' ' || remainder[0] is not ('1' or '2'))
        { return null; }
        var number = remainder[0] == '1' ? 1 : 2;
        var operation = remainder[2..];
        var separator = operation.IndexOf(' ');
        var verb = separator < 0 ? operation : operation[..separator];
        var argument = separator < 0 ? string.Empty : operation[(separator + 1)..];
        var action = verb.ToUpperInvariant() switch
        {
            "SELECT" => "Select", "SOURCE" => "Source", "REQUEST" => "Request", "TARGET" => "Target",
            "TARGETS" => "Targets", "NEXT" => "NextTargets", "REVIEW" => "Review", "RESOLVE" => "Resolve",
            "CONFIRM" => "Invoke", "STATUS" => "Status", "STOP" => "Stop", _ => "Invalid",
        };
        if (action is "Invalid") { return new("Invalid"); }
        if (action is "Source" or "Request" or "Target" ? argument.Length == 0 : argument.Length != 0)
        { return new("Invalid"); }
        return new(action, number, argument);
    }
}
