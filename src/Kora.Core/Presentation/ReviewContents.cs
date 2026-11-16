using System.Collections.Immutable;

using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Skills;

namespace Kora.Core.Presentation;

/// <summary>
/// Read-only copy of the exact files an item under review will use. Viewing it grants no approval,
/// enablement or execution authority; the owning review keeps its own confirmation.
/// </summary>
public sealed class ReviewContents
{
    public const int MaximumDocuments = 256;

    public ReviewContents(string title, string revision, IEnumerable<ReviewDocument> documents,
        IEnumerable<string>? unavailable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(documents);
        Title = title;
        Revision = revision;
        Documents = [.. documents];
        if (Documents.IsEmpty) { throw new ArgumentException("Review contents require at least one document.", nameof(documents)); }
        if (Documents.Length > MaximumDocuments)
        { throw new ArgumentException($"Review contents are limited to {MaximumDocuments} documents.", nameof(documents)); }
        if (Documents.Select(document => document.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Documents.Length)
        { throw new ArgumentException("Review document paths must be unique.", nameof(documents)); }
        Unavailable = unavailable is null ? [] : [.. unavailable.Where(item => !string.IsNullOrWhiteSpace(item))];
    }

    public string Title { get; }
    public string Revision { get; }
    public ImmutableArray<ReviewDocument> Documents { get; }

    /// <summary>Declared files that were not captured, shown so an incomplete set is never presented as complete.</summary>
    public ImmutableArray<string> Unavailable { get; }

    public static ReviewContents FromText(string title, string path, string text, string? revision = null)
    {
        var document = ReviewDocument.FromText(path, text);
        return new(title, revision ?? document.Digest, [document]);
    }

    public static ReviewContents FromPackage(string title, SkillPackageSnapshot package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new(title, package.DefinitionDigest,
            package.Files.Select(file => ReviewDocument.FromBytes(file.Name, file.Bytes)));
    }

    public static ReviewContents FromSharedSkill(string title, SharedSkillSnapshot skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        var documents = new List<ReviewDocument> { ReviewDocument.FromBytes(FileName(skill.RelativeFile), skill.Bytes) };
        if (skill.ReferenceBundle is { } bundle)
        { documents.AddRange(bundle.References.Select(reference => ReviewDocument.FromBytes(reference.RelativePath, reference.Bytes))); }
        var primary = Slashes(skill.RelativeFile);
        var directory = primary[..(primary.LastIndexOf('/') + 1)];
        var captured = skill.ReferenceBundle?.References.Select(reference => directory + Slashes(reference.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        var unavailable = skill.UninspectedFiles.Where(file => !captured.Contains(Slashes(file)));
        if (skill.ReferenceDeclaration is { } declaration && skill.ReferenceBundle is null)
        { unavailable = unavailable.Concat(declaration.Paths.Select(path => path + " (reference not read)")); }
        return new(title, skill.ReferenceBundle?.RevisionDigest ?? skill.RevisionDigest, documents, unavailable);
    }

    public static ReviewContents FromOwnedSkill(string title, OwnedDeclarativeSkillSnapshot skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        var document = skill.Document;
        var documents = new List<ReviewDocument>
        {
            ReviewDocument.FromBytes("manifest.yaml", document.ManifestBytes.Span),
            ReviewDocument.FromBytes("instructions.md", document.InstructionBytes.Span),
        };
        documents.AddRange(document.References.Select(reference => ReviewDocument.FromBytes(reference.RelativePath, reference.Bytes)));
        var unavailable = document.ReferenceDeclaration?.Paths
            .Where(path => document.References.All(reference => !string.Equals(reference.RelativePath, path, StringComparison.Ordinal)))
            .Select(path => path + " (reference not read)");
        return new(title, document.RevisionDigest, documents, unavailable);
    }

    public static ReviewContents FromAgent(string title, BoundedAgentProfile profile, AgentInstructionBundle? instructions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var documents = new List<ReviewDocument>
        { ReviewDocument.FromBytes(profile.Document.Definition.Id + ".agent.md", profile.Document.CopyBytes()) };
        if (instructions is not null)
        {
            documents.AddRange(instructions.Instructions.Select(instruction =>
                ReviewDocument.FromBytes("instructions/" + instruction.CanonicalFileName, instruction.Document.CopyBytes())));
        }
        var unavailable = instructions is null && profile.References.Length > 0
            ? profile.References.Select(reference => reference.Id + " (instruction not resolved yet)") : null;
        return new(title, profile.Document.Definition.Digest, documents, unavailable);
    }

    private static string Slashes(string path) => path.Replace('\\', '/');

    private static string FileName(string relativeFile) =>
        relativeFile.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? relativeFile;
}
