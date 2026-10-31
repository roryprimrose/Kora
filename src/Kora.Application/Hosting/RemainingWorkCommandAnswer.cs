using System.Collections.Immutable;
using System.Globalization;

using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed record RemainingWorkCommandAnswer(SessionRemainingWorkAnswer? Answer,
    ImmutableArray<SessionWorkspaceEntry> Choices, string Message)
{
    public string Format() => Choices.IsEmpty ? Message : Message + "\n"
        + string.Join("\n", Choices.Select((entry, index) => (index + 1).ToString(CultureInfo.InvariantCulture)
            + ". " + entry.Metadata!.Name.Value + (entry.Authority.IsActive ? " (Active)" : " (Done)")))
        + "\nUse session remaining choose <number>, or session remaining cancel. If the rows are indistinguishable, cancel and select the exact native conversation.";
}