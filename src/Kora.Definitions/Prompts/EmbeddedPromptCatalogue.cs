using System.Collections.Immutable;
using Kora.Core.Artifacts;

namespace Kora.Definitions.Prompts;

public static class EmbeddedPromptCatalogue
{
    public const string ResourceId = "Kora.Prompts.RewriteForClarity";
    private const string ReviewedDigest = "824ac4c32d5907add04e1b06dfe4ddf59dc94f632850b376fe843683f4a3e295";
    private static readonly ImmutableArray<BundledPromptSource> Sources = ReadAssembly();

    public static ImmutableArray<BundledPromptSource> Load() => Sources;

    private static ImmutableArray<BundledPromptSource> ReadAssembly()
    {
        var assembly = typeof(EmbeddedPromptCatalogue).Assembly;
        return LoadResources(assembly.GetManifestResourceNames(), assembly.GetManifestResourceStream);
    }

    internal static ImmutableArray<BundledPromptSource> LoadResources(
        IReadOnlyList<string> resourceNames, Func<string, Stream?> openResource)
    {
        var names = resourceNames.Where(name => name.StartsWith("Kora.Prompts.", StringComparison.Ordinal)).ToArray();
        if (names.Length != 1 || !string.Equals(names[0], ResourceId, StringComparison.Ordinal))
        { throw new InvalidDataException("The assembly resource map differs from the explicit bundled prompt registration."); }
        using var stream = openResource(ResourceId)
            ?? throw new InvalidDataException("The registered bundled prompt resource is missing.");
        if (stream.Length is 0 or > ArtifactDefinition.MaximumContentLength)
        { throw new InvalidDataException("The bundled prompt resource exceeds its byte bounds."); }
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var prompt = new BundledPromptSource("kora.prompt.rewrite-for-clarity", ResourceId, bytes);
        if (!string.Equals(prompt.Definition.Digest, ReviewedDigest, StringComparison.Ordinal))
        { throw new InvalidDataException("The bundled prompt bytes differ from the reviewed registration."); }
        return [prompt];
    }
}
