using System.Collections.ObjectModel;

using Kora.Core.Authorization;

namespace Kora.Core.Skills;

public sealed class SkillPackageSnapshot
{
    internal SkillPackageSnapshot(SkillResourceSnapshot manifestFile, IReadOnlyDictionary<string, SkillResourceSnapshot> resources)
    {
        Manifest = SkillPackageManifest.Parse(manifestFile);
        var files = new List<SkillResourceSnapshot> { manifestFile };
        foreach (var declared in Manifest.Files)
        {
            if (!resources.TryGetValue(declared.ResourceId, out var resource)
                || !string.Equals(resource.Name, declared.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A declared resource is missing or its exact logical name/resource mapping differs.");
            }
            files.Add(resource);
        }
        Files = files.AsReadOnly();
        DeclaredResourceDigest = SkillPackageDigest.Compute(SkillPackageDigest.DeclaredResourcesVersion, Files);
        ScriptSetDigest = SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion,
            files.Where(file => Manifest.Files.Any(declaration =>
                string.Equals(declaration.Name, file.Name, StringComparison.Ordinal)
                && declaration.Kind is "entry" or "helper")).ToArray());
        DefinitionDigest = SkillPackageDigest.Compute(SkillPackageDigest.DefinitionVersion,
            files.Where(file => !file.Name.EndsWith(".ps1", StringComparison.Ordinal)).ToArray());
    }

    public SkillPackageManifest Manifest { get; }
    public ReadOnlyCollection<SkillResourceSnapshot> Files { get; }
    public string ScriptSetDigest { get; }
    public string DefinitionDigest { get; }
    public string DeclaredResourceDigest { get; }
    public bool IsAvailableForInvocation => false;
    public const string UnavailableReason = "Inspection only. R11 worker, protected deployment, network and real-control admission are outstanding. No interpreter or adapter identity is admitted.";
    public const string TransitiveDisclosure = "Declared files are byte-exact. Transitive tracking is best effort, not a complete inventory or executable allowlist. Further scripts, modules, binaries and changes may go undetected. A future granting user accepts responsibility for the overall actions within separately admitted scope. This review grants nothing.";
    public const string TransitiveGaps = "Tracked transitive code: none admitted. The dynamic SessionControl.Invoke adapter call is unresolved until separate host admission; the requested runtime and adapter declarations are not verified identities.";

    // Only compares the declared-content portion of an existing exact record.
    // It deliberately does not claim interpreter, invocation, identity or grant validity.
    public bool HasSameDeclaredContent(ExactOperationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return string.Equals(binding.SourcePartition, "bundled", StringComparison.Ordinal)
            && string.Equals(binding.SkillId, Manifest.Id, StringComparison.Ordinal)
            && string.Equals(binding.ActionId, Manifest.Action, StringComparison.Ordinal)
            && string.Equals(binding.DefinitionDigest, DefinitionDigest, StringComparison.Ordinal)
            && string.Equals(binding.DeclaredResourceDigest, DeclaredResourceDigest, StringComparison.Ordinal);
    }
}
