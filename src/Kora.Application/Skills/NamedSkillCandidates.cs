using System.Collections.ObjectModel;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>A frozen, expiring choice set. It retains canonical snapshots, not another model metadata projection.</summary>
public sealed class NamedSkillCandidates : IDisposable
{
    private readonly Func<bool> current;
    private readonly TimeProvider time;
    private readonly DateTimeOffset created;
    private ReadOnlyCollection<NamedSkillChoice> choices;
    private int retired;
    private int chosen;

    internal NamedSkillCandidates(string name, IReadOnlyList<OwnedDeclarativeSkillSnapshot> owned,
        IReadOnlyList<SharedSkillCatalogue> shared, OwnedSkillAuthoringAuthority original, Func<bool> current,
        TimeProvider time, Func<OwnedDeclarativeSkillSnapshot, bool> ownedAllowed,
        Func<SharedSkillSource, SharedSkillSnapshot, bool> sharedAllowed)
    {
        this.current = current;
        this.time = time;
        created = time.GetUtcNow();
        var matches = new List<NamedSkillChoice>();
        foreach (var snapshot in owned)
        {
            if (string.Equals(snapshot.Document.Name, name, StringComparison.Ordinal))
            { matches.Add(new(matches.Count + 1, snapshot, null, null, "Owned",
                snapshot.Document.Tool == OwnedSkillToolSelection.None && ownedAllowed(snapshot), original, () => IsCurrent)); }
        }
        for (var index = 0; index < shared.Count; index++)
        {
            var catalogue = shared[index];
            foreach (var snapshot in catalogue.Packages)
            {
                if (string.Equals(snapshot.Name, name, StringComparison.Ordinal))
                {
                    var supported = snapshot.IsInstructionCompatible && sharedAllowed(catalogue.Source, snapshot);
                    matches.Add(new(matches.Count + 1, null, catalogue.Source, snapshot,
                        $"Shared source {index + 1} ({catalogue.Source.ProfileRelativeRoot})", supported, original, () => IsCurrent));
                }
            }
        }
        choices = matches.AsReadOnly();
    }

    public bool IsCurrent
    {
        get
        {
            if (!current() || time.GetUtcNow() < created || time.GetUtcNow() >= created.AddMinutes(2)) { Dispose(); }
            return Volatile.Read(ref retired) == 0;
        }
    }

    public IReadOnlyList<NamedSkillChoice> Choices => IsCurrent ? choices : [];

    public NamedSkillChoice Choose(int oneBasedChoice)
    {
        if (!IsCurrent) { throw new InvalidOperationException("The exact inspected choices expired or were revoked. Inspect again."); }
        if (oneBasedChoice < 1 || oneBasedChoice > choices.Count)
        { throw new InvalidOperationException("Choose one numbered source-qualified row from the current displayed choices."); }
        var choice = choices[oneBasedChoice - 1];
        if (!choice.IsSupported)
        { throw new InvalidOperationException("That exact source is excluded or not a supported instruction-only skill. No other source or executable fallback was selected."); }
        if (Interlocked.Exchange(ref chosen, 1) != 0)
        { throw new InvalidOperationException("This exact choice set has already been used. Start a new named request."); }
        return choice;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref retired, 1);
        choices = Array.AsReadOnly<NamedSkillChoice>([]);
    }
}
