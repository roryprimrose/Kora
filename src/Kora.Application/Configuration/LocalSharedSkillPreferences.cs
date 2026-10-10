using System.Text.Json;

using Kora.Core.Dependencies;
using Kora.Core.Skills;

namespace Kora.Application.Configuration;

public sealed class LocalSharedSkillPreferences
{
    private const string FileName = "shared-skill-sources.json";
    private const int MaximumPreferenceBytes = 4096;
    private readonly IPreferenceStore store;
    private readonly Lock gate = new();

    public LocalSharedSkillPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalSharedSkillPreferences(IPreferenceStore store) => this.store = store;

    public IReadOnlyList<SharedSkillSource> Load()
    {
        lock (gate) { return LoadCore(); }
    }

    internal T WithUnchangedSources<T>(IReadOnlyList<SharedSkillSource> expected, Func<T> mutation)
    {
        lock (gate)
        {
            Validate(expected);
            if (!LoadCore().SequenceEqual(expected))
            { throw new InvalidOperationException("Source preferences changed; refresh and confirm again."); }
            return mutation();
        }
    }

    private IReadOnlyList<SharedSkillSource> LoadCore()
    {
        var text = store.ReadText(FileName, MaximumPreferenceBytes);
        if (text is null) { return Array.Empty<SharedSkillSource>(); }
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            RequireProperties(root, "Version", "Sources");
            if (root.GetProperty("Version").GetInt32() != 1)
            { throw new InvalidDataException("The shared source preference version is unavailable."); }
            var sources = root.GetProperty("Sources");
            if (sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() > SharedSkillSource.MaximumSources)
            { throw new InvalidDataException("The shared source preference is not a bounded array."); }
            var result = new List<SharedSkillSource>();
            foreach (var item in sources.EnumerateArray())
            {
                RequireProperties(item, "Id", "ProfileRelativeRoot", "DirectoryIdentity");
                var id = item.GetProperty("Id").GetString();
                if (!Guid.TryParseExact(id, "N", out var parsed))
                { throw new InvalidDataException("The source identity is invalid."); }
                result.Add(new(parsed, item.GetProperty("ProfileRelativeRoot").GetString()!,
                    item.GetProperty("DirectoryIdentity").GetString()!));
            }
            Validate(result);
            return result.AsReadOnly();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException
            or ArgumentException)
        { throw new InvalidDataException("The saved shared source registration is invalid. No shared sources may be read; restore a verified preference file.", exception); }
    }

    public void Save(IReadOnlyList<SharedSkillSource> sources)
    {
        lock (gate) { SaveCore(sources); }
    }

    private void SaveCore(IReadOnlyList<SharedSkillSource> sources)
    {
        Validate(sources);
        var text = JsonSerializer.Serialize(new
        {
            Version = 1,
            Sources = sources.Select(source => new
            {
                Id = source.Id.ToString("N"),
                source.ProfileRelativeRoot,
                source.DirectoryIdentity,
            }),
        });
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaximumPreferenceBytes)
        { throw new InvalidDataException("The source registrations exceed the persisted byte limit."); }
        store.WriteText(FileName, text);
    }

    private static void Validate(IReadOnlyList<SharedSkillSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count > SharedSkillSource.MaximumSources || sources.Any(source => source is null)
            || sources.Select(source => source.Id).Distinct().Count() != sources.Count
            || sources.Select(source => source.ProfileRelativeRoot).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count
            || sources.Select(source => source.DirectoryIdentity).Distinct(StringComparer.Ordinal).Count() != sources.Count)
        { throw new InvalidDataException("Duplicate, aliased or oversized source registrations are not permitted."); }
    }

    private static void RequireProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        { throw new InvalidDataException("The preference contains missing, duplicate or unknown properties."); }
    }
}
