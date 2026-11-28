using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Conversation;

namespace Kora.Core.Commands;

/// <summary>An exact typed or spoken answer to the single current running-turn check-in.</summary>
public sealed record TurnCheckInReply(ConversationCheckInChoice? Choice)
{
    private const string KeepGoingFor = "keep going for ";

    public const string Syntax = "keep going | keep going for <1-60> minutes | keep going until done | stop this turn";

    public static IReadOnlyList<string> FixedPhrases { get; } = ["keep going", "keep going until done", "stop this turn"];

    public static TurnCheckInReply? Parse(string input, string assistantName)
    {
        var text = input.Trim().TrimEnd('.', '?', '!').TrimEnd();
        var name = AssistantNameRules.ResolveActivationName(text, assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ','); }
        if (!text.StartsWith("keep going", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "stop this turn", StringComparison.OrdinalIgnoreCase))
        { return null; }
        if (input.Length > 1024 || input.Any(char.IsControl)) { return new(Choice: null); }
        switch (text.ToLowerInvariant())
        {
            case "keep going": return new(new ConversationCheckInChoice(ConversationCheckInOperation.KeepGoing));
            case "keep going until done": return new(new ConversationCheckInChoice(ConversationCheckInOperation.KeepRunning));
            case "stop this turn": return new(new ConversationCheckInChoice(ConversationCheckInOperation.Stop));
        }
        if (!text.StartsWith(KeepGoingFor, StringComparison.OrdinalIgnoreCase)) { return new(Choice: null); }
        var parts = text[KeepGoingFor.Length..].Split(' ');
        if (parts.Length != 2 || !(parts[1].Equals("minutes", StringComparison.OrdinalIgnoreCase)
                || parts[1].Equals("minute", StringComparison.OrdinalIgnoreCase) && string.Equals(parts[0], "1", StringComparison.Ordinal))
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes is < OrdinaryTurnDeadline.MinimumMinutes or > OrdinaryTurnDeadline.MaximumMinutes
            || !string.Equals(parts[0], minutes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        { return new(Choice: null); }
        return new(new ConversationCheckInChoice(ConversationCheckInOperation.KeepGoingFor, new OrdinaryTurnDeadline(minutes)));
    }
}
