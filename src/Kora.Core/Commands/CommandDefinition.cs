namespace Kora.Core.Commands;

public sealed record CommandDefinition(
    BuiltInAction Action,
    string CanonicalPhrase,
    string Description,
    IReadOnlyList<string> Aliases)
{
    public bool SupportsModelSuggestion => Action is not (BuiltInAction.DisableListening or BuiltInAction.StopAllWork);

    public IEnumerable<string> AllPhrases
    {
        get
        {
            yield return CanonicalPhrase;

            foreach (var alias in Aliases)
            {
                yield return alias;
            }
        }
    }
}