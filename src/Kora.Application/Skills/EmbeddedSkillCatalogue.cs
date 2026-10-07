using Kora.Core.Skills;

namespace Kora.Application.Skills;

public static class EmbeddedSkillCatalogue
{
    private static readonly (string Name, string Id)[] Registrations =
    [
        ("skills\\session-lock\\manifest.json", "Kora.Skills.Session.Lock.Manifest"),
        ("skills\\session-lock\\skill.md", "Kora.Skills.Session.Lock.Instructions"),
        ("skills\\session-lock\\fixtures.json", "Kora.Skills.Session.Lock.Fixtures"),
        ("scripts\\session\\lock.ps1", "Kora.Scripts.Session.Lock"),
        ("skills\\session-shutdown\\manifest.json", "Kora.Skills.Session.Shutdown.Manifest"),
        ("skills\\session-shutdown\\skill.md", "Kora.Skills.Session.Shutdown.Instructions"),
        ("skills\\session-shutdown\\fixtures.json", "Kora.Skills.Session.Shutdown.Fixtures"),
        ("scripts\\session\\shutdown.ps1", "Kora.Scripts.Session.Shutdown"),
        ("skills\\session-restart\\manifest.json", "Kora.Skills.Session.Restart.Manifest"),
        ("skills\\session-restart\\skill.md", "Kora.Skills.Session.Restart.Instructions"),
        ("skills\\session-restart\\fixtures.json", "Kora.Skills.Session.Restart.Fixtures"),
        ("scripts\\session\\restart.ps1", "Kora.Scripts.Session.Restart"),
        ("scripts\\shared\\session-control.ps1", "Kora.Scripts.Shared.SessionControl"),
    ];

    public static SkillPackageCatalogue Load()
    {
        var assembly = typeof(EmbeddedSkillCatalogue).Assembly;
        return LoadResources(assembly.GetManifestResourceNames(), assembly.GetManifestResourceStream);
    }

    internal static SkillPackageCatalogue LoadResources(IReadOnlyList<string> resourceNames, Func<string, Stream?> openResource)
    {
        var names = resourceNames.Where(name =>
            name.StartsWith("Kora.Skills.", StringComparison.Ordinal)
            || name.StartsWith("Kora.Scripts.", StringComparison.Ordinal)).ToArray();
        var uniqueNames = names.ToHashSet(StringComparer.Ordinal);
        if (uniqueNames.Count != names.Length || !uniqueNames.SetEquals(Registrations.Select(item => item.Id)))
        {
            throw new InvalidDataException("The first-party assembly resource map differs from the fixed skill catalogue.");
        }
        var files = new List<SkillResourceSnapshot>();
        foreach (var (name, id) in Registrations)
        {
            using var stream = openResource(id)
                ?? throw new InvalidDataException("A fixed embedded skill resource is missing.");
            if (stream.Length is 0 or > SkillResourceSnapshot.MaximumBytes)
            {
                throw new InvalidDataException("An embedded skill resource exceeds its byte bounds.");
            }
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            files.Add(new(name, id, bytes));
        }
        return new(Registrations.Where(item => item.Name.EndsWith("\\manifest.json", StringComparison.Ordinal))
            .Select(item => item.Id).ToArray(), files);
    }
}
