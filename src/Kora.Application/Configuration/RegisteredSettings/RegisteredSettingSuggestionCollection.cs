using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.Configuration.RegisteredSettings;

/// <summary>Complete inert answer data. Selection never grants review or write authority.</summary>
public sealed class RegisteredSettingSuggestionCollection : IReadOnlyCollection<RegisteredSettingProposal>
{
    public const int MaximumMembers = 8;
    public const int MaximumAnswerLength = 3072;

    private RegisteredSettingSuggestionCollection(RegisteredSettingProposal[] members) =>
        Members = Array.AsReadOnly(members);

    public IReadOnlyList<RegisteredSettingProposal> Members { get; }
    public int Count => Members.Count;
    public IEnumerator<RegisteredSettingProposal> GetEnumerator() => Members.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public static RegisteredSettingSuggestionCollection Parse(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Length > MaximumAnswerLength)
        { throw new InvalidDataException("The complete setting answer exceeds 3072 characters; nothing was selected."); }
        var lines = answer.Split('\n');
        if (lines.Length > MaximumMembers)
        { throw new InvalidDataException("A setting collection contains at most eight unique registered members."); }
        var members = new RegisteredSettingProposal[lines.Length];
        var identities = new HashSet<RegisteredSetting>();
        for (var index = 0; index < lines.Length; index++)
        {
            RegisteredSettingProposal proposal;
            try { proposal = RegisteredSettingSuggestions.Parse(lines[index]); }
            catch (ArgumentException exception)
            { throw new InvalidDataException("The complete setting collection contains an invalid member.", exception); }
            if (!identities.Add(proposal.Setting)
                || lines.Length > 1 && !string.Equals(lines[index], CanonicalCommand(proposal, lines[index]), StringComparison.Ordinal))
            { throw new InvalidDataException("Every collection member must be canonical with a unique exact registered ID."); }
            members[index] = proposal;
        }
        return new(members);
    }

    public static RegisteredSettingProposal ParseSelected(string answer, RegisteredSetting? selectedSetting)
    {
        if (selectedSetting is null) { return RegisteredSettingSuggestions.Parse(answer); }
        return Parse(answer).Members.SingleOrDefault(item => item.Setting == selectedSetting)
            ?? throw new InvalidDataException("Deliberately select one member of the complete original setting answer.");
    }

    // These two legacy single-command parsers admit aliases; collection members do not.
    private static string CanonicalCommand(RegisteredSettingProposal proposal, string command) => proposal switch
    {
        RegisteredSettingProposal.Appearance appearance =>
            $"set {proposal.Descriptor.Id} to {AppearanceCommand.Format(appearance.Value)}",
        RegisteredSettingProposal.Summary summary => $"set {proposal.Descriptor.Id} to {summary.Value}",
        _ => command,
    };
}
