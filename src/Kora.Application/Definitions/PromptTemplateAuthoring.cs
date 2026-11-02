using System.Collections.Immutable;
using System.Text.Json;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public static class PromptTemplateAuthoring
{
    public static ImmutableArray<string> RequiredNames(string declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (declaration.Length > OwnedPromptTemplate.MaximumFields * (OwnedPromptTemplate.MaximumNameLength + 1))
        { throw new InvalidDataException("Declare at most eight bounded literal input names."); }
        if (declaration.Length == 0) { return []; }
        var names = declaration.Split(',');
        var parsed = OwnedPromptTemplate.Parse(string.Join("\n", names.Select(name => "${input:" + name + "}"))).RequiredNames;
        if (!parsed.SequenceEqual(names, StringComparer.Ordinal))
        { throw new InvalidDataException("Declare distinct literal names in first-use order, comma-separated without spaces or marker syntax."); }
        return parsed;
    }

    public static ArtifactFileDocument Prepare(CustomizationSource source, IReadOnlyList<CustomizationFile> files,
        string declaration)
    {
        if (source is not { Kind: CustomizationKind.Prompt, Scope: CustomizationScope.Kora, IsReadOnly: false })
        { throw new InvalidDataException("Prompt template authoring supports Kora-owned saved USER requests only, not profile originals."); }
        _ = CustomizationSnapshot.ValidateFiles(source, files);
        var primary = files.Single();
        var document = ArtifactFileDocument.Parse(primary.CopyBytes(), ArtifactKind.Prompt, "kora-profile", source.Name);
        document.RequireEditableInstruction();
        var required = RequiredNames(declaration);
        if (!required.IsEmpty && document.Definition.PromptTemplate is null)
        { throw new InvalidDataException("Required literal inputs need explicit native kora-format: 2. Existing v1 metadata is not upgraded by generation."); }
        return document;
    }

    public static CustomizationAuthoringResult Parse(CustomizationSource source, IReadOnlyList<CustomizationFile> files,
        CustomizationAuthoringContext context, string answer)
    {
        var original = Prepare(source, files, context.RequiredPromptInputs
            ?? throw new InvalidDataException("Review an explicit host input declaration before prompt authoring."));
        try
        {
            using var json = JsonDocument.Parse(answer, new JsonDocumentOptions { MaxDepth = 2 });
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.EnumerateObject().Count() == 1
                && json.RootElement.TryGetProperty("unsupported", out var unsupported) && unsupported.ValueKind == JsonValueKind.String)
            { throw new InvalidOperationException("The requested prompt capability is unsupported. Draft retained; no tools, reads, expressions or workflow were added."); }
        }
        catch (JsonException exception)
        { throw new InvalidDataException("Invalid prompt proposal JSON. The draft is unchanged.", exception); }
        var body = DefinitionBodyProposal.Parse(answer);
        var proposed = original.WithPromptBody(body);
        proposed.RequireEditableInstruction();
        var names = proposed.Definition.PromptTemplate?.RequiredNames ?? [];
        if (!names.SequenceEqual(RequiredNames(context.RequiredPromptInputs), StringComparer.Ordinal))
        { throw new InvalidDataException("The proposed template must use exactly the host-declared required literals in first-use order. Nothing was applied."); }
        if (context.Mode == CustomizationAuthoringMode.Discuss
            && !proposed.CopyBytes().AsSpan().SequenceEqual(original.CopyBytes()))
        { throw new InvalidDataException("Prompt discussion must leave the complete source unchanged."); }
        return new("Saved USER request template only. Required literals: " + (names.IsEmpty ? "(none)" : string.Join(", ", names))
            + ". No expressions, defaults, recursive expansion, tools or instruction authority. Save and use remain separate.",
            [new(source.PrimaryFile, proposed.CopyBytes())]);
    }
}
