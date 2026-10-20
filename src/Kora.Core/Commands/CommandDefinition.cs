namespace Kora.Core.Commands;

public sealed record CommandDefinition(
    BuiltInAction Action,
    string CanonicalPhrase,
    string Description,
    IReadOnlyList<string> Aliases)
{
    public bool SupportsModelSuggestion => Action != BuiltInAction.DisableListening;

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