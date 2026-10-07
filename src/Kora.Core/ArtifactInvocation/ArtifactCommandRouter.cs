using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Core.Artifacts;

public sealed class ArtifactCommandRouter(ArtifactCatalogue catalogue)
{
    public ArtifactCommandMatch Match(string input, string assistantName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var trimmed = input.Trim();
        if (trimmed.StartsWith('/'))
        {
            return MatchSlashCommand(trimmed);
        }

        var withoutActivation = RemoveActivationPrefix(trimmed, assistantName);
        foreach (var artifact in catalogue.Artifacts)
        {
            foreach (var spokenName in artifact.SpokenNames.OrderByDescending(name => name.Length))
            {
                foreach (var prefix in SpokenPrefixes(spokenName, artifact.Kind))
                {
                    if (TryMatchSpoken(withoutActivation, prefix, out var request))
                    {
                        return new(true, new ArtifactInvocation(artifact, request), null);
                    }
                }
            }
        }
        return ArtifactCommandMatch.NotMatched;
    }

    private ArtifactCommandMatch MatchSlashCommand(string input)
    {
        var body = input[1..].TrimStart();
        if (body.Length == 0)
        {
            return new(true, null, "Enter an artifact command after “/”.");
        }
        var firstSeparator = body.IndexOfAny([' ', '\t', '\r', '\n']);
        var first = firstSeparator < 0 ? body : body[..firstSeparator];
        var remainder = firstSeparator < 0 ? string.Empty : body[(firstSeparator + 1)..].Trim();
        ArtifactKind? kind = first.ToLowerInvariant() switch
        {
            "skill" => ArtifactKind.Skill,
            "instruction" or "instructions" => ArtifactKind.Instruction,
            "prompt" => ArtifactKind.Prompt,
            _ => null,
        };
        string commandName;
        string request;
        if (kind is null)
        {
            commandName = first;
            request = remainder;
        }
        else
        {
            if (remainder.Length == 0)
            {
                return new(true, null, $"Enter a {kind.ToString()!.ToLowerInvariant()} command name.");
            }
            var separator = remainder.IndexOfAny([' ', '\t', '\r', '\n']);
            commandName = separator < 0 ? remainder : remainder[..separator];
            request = separator < 0 ? string.Empty : remainder[(separator + 1)..].Trim();
        }

        var artifact = catalogue.Artifacts.SingleOrDefault(candidate =>
            (kind is null || candidate.Kind == kind)
            && string.Equals(candidate.CommandName, commandName, StringComparison.OrdinalIgnoreCase));
        return artifact is null
            ? new(true, null, $"No available artifact uses the command “/{commandName}”.")
            : new(true, new ArtifactInvocation(artifact, request), null);
    }

    private static IEnumerable<string> SpokenPrefixes(string spokenName, ArtifactKind kind)
    {
        var kindName = kind.ToString().ToLowerInvariant();
        yield return $"run {spokenName}";
        yield return $"use {spokenName}";
        yield return $"run {spokenName} {kindName}";
        yield return $"use {spokenName} {kindName}";
        yield return $"run the {spokenName} {kindName}";
        yield return $"use the {spokenName} {kindName}";
    }

    private static bool TryMatchSpoken(string input, string prefix, out string request)
    {
        var trimmed = input.Trim().TrimEnd('.', '!', '?');
        if (string.Equals(trimmed, prefix, StringComparison.OrdinalIgnoreCase))
        {
            request = string.Empty;
            return true;
        }
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            request = string.Empty;
            return false;
        }
        var remainder = trimmed[prefix.Length..];
        foreach (var separator in new[] { " with ", " to ", ": " })
        {
            if (remainder.StartsWith(separator, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(remainder[separator.Length..]))
            {
                request = remainder[separator.Length..].Trim();
                return true;
            }
        }
        request = string.Empty;
        return false;
    }

    private static string RemoveActivationPrefix(string input, string assistantName)
    {
        var name = AssistantNameRules.Normalize(assistantName);
        if (!input.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return input;
        }
        var remainder = input[name.Length..];
        if (remainder.Length == 0 || remainder[0] is not (' ' or ',' or ':' or '-' or '—'))
        {
            return input;
        }
        return remainder.TrimStart(' ', ',', ':', '-', '—');
    }
}
