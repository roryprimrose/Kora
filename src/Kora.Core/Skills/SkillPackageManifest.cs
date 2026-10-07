using System.Collections.ObjectModel;
using System.Text.Json;

using Kora.Core.Interaction;

namespace Kora.Core.Skills;

// This is the fixed first-party inspection schema, not a YAML/plugin or executable loader.
public sealed class SkillPackageManifest
{
    private SkillPackageManifest(JsonElement root)
    {
        Object(root, "schemaVersion", "id", "version", "name", "description", "action",
            "entryPoint", "parameterContract", "runtime", "dependencies", "helperLoadOrder", "files");
        if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number
            || !root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1)
        {
            throw new InvalidDataException("Unsupported embedded skill manifest schema.");
        }
        Id = InteractionValidation.Identifier(String(root.GetProperty("id")));
        Version = InteractionValidation.Identifier(String(root.GetProperty("version")));
        Name = InteractionValidation.Text(String(root.GetProperty("name")), 128);
        Description = InteractionValidation.Text(String(root.GetProperty("description")), 512);
        Action = InteractionValidation.Identifier(String(root.GetProperty("action")));
        EntryPoint = SkillResourceSnapshot.ValidateName(String(root.GetProperty("entryPoint")));
        ParameterContract = InteractionValidation.Identifier(String(root.GetProperty("parameterContract")));
        Runtime = InteractionValidation.Identifier(String(root.GetProperty("runtime")));
        Dependencies = Array(root.GetProperty("dependencies"), 8).Select(item =>
            InteractionValidation.Identifier(String(item))).ToList().AsReadOnly();
        HelperLoadOrder = Array(root.GetProperty("helperLoadOrder"), SkillPackageDigest.MaximumFiles)
            .Select(item => SkillResourceSnapshot.ValidateName(String(item))).ToList().AsReadOnly();
        Files = Array(root.GetProperty("files"), SkillPackageDigest.MaximumFiles - 1).Select(item =>
        {
            Object(item, "name", "resourceId", "kind");
            return new SkillFileDeclaration(SkillResourceSnapshot.ValidateName(String(item.GetProperty("name"))),
                String(item.GetProperty("resourceId")), String(item.GetProperty("kind")));
        }).ToList().AsReadOnly();
        if (Dependencies.Count == 0 || Dependencies.Distinct(StringComparer.Ordinal).Count() != Dependencies.Count
            || Files.Count == 0
            || Files.Count(file => file.Kind is "instructions") != 1
            || Files.Any(file => file.Kind is not ("instructions" or "fixture" or "entry" or "helper"))
            || Files.Any(file => !file.Name.EndsWith(file.Kind switch
                { "instructions" => ".md", "fixture" => ".json", _ => ".ps1" }, StringComparison.Ordinal))
            || Files.Count(file => file.Kind is "entry" && string.Equals(file.Name, EntryPoint, StringComparison.Ordinal)) != 1
            || Files.Count(file => file.Kind is "entry") != 1
            || !HelperLoadOrder.ToHashSet(StringComparer.Ordinal).SetEquals(
                Files.Where(file => file.Kind is "helper").Select(file => file.Name))
            || HelperLoadOrder.Distinct(StringComparer.Ordinal).Count() != HelperLoadOrder.Count)
        {
            throw new InvalidDataException("The fixed manifest requires exact instruction, entry and helper roles, dependencies and load order.");
        }
    }

    public string Id { get; }
    public string Version { get; }
    public string Name { get; }
    public string Description { get; }
    public string Action { get; }
    public string EntryPoint { get; }
    public string ParameterContract { get; }
    public string Runtime { get; }
    public bool IsActionAvailableForInvocation => false;
    public ReadOnlyCollection<string> Dependencies { get; }
    public ReadOnlyCollection<string> HelperLoadOrder { get; }
    public ReadOnlyCollection<SkillFileDeclaration> Files { get; }

    public static SkillPackageManifest Parse(SkillResourceSnapshot manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        try
        {
            // The optional BOM is ignored only by the parser, never by hashing or review.
            using var document = JsonDocument.Parse(manifest.Text.StartsWith('\uFEFF') ? manifest.Text[1..] : manifest.Text,
                new JsonDocumentOptions { MaxDepth = 8 });
            return new(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The embedded skill manifest is invalid JSON.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The embedded skill manifest contains an invalid domain value.", exception);
        }
    }

    private static void Object(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object
            || element.EnumerateObject().Count() != fields.Length
            || !element.EnumerateObject().Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal).SetEquals(fields))
        {
            throw new InvalidDataException("Manifest fields must match the explicit schema without missing, duplicate or unknown fields.");
        }
    }

    private static string String(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidDataException("A nonempty manifest string is required.");
        }
        return element.GetString()!;
    }

    private static JsonElement[] Array(JsonElement element, int maximum)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > maximum)
        {
            throw new InvalidDataException("Manifest arrays must be explicit and bounded.");
        }
        return [.. element.EnumerateArray()];
    }
}
