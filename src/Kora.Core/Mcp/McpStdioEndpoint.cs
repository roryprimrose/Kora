using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Kora.Core.Conversation;

namespace Kora.Core.Mcp;

/// <summary>A credential-free literal local launch profile; validation does not authorize execution.</summary>
public sealed record McpStdioEndpoint
{
    public McpStdioEndpoint(string executable, IEnumerable<string> arguments, string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        RequirePath(executable);
        RequirePath(workingDirectory);
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Stdio requires an installed local executable, not a shell or installer command."); }
        var args = arguments.ToImmutableArray();
        if (args.Length > 64 || args.Any(value => value is null || value.Length > 2048 || value.Any(char.IsControl))
            || args.Sum(value => value.Length) > 8192 || environment.Count > 16)
        { throw new InvalidDataException("The literal stdio argument or environment profile exceeds its bound."); }
        foreach (var value in args) { ConversationText.RequireNoCredentials(value); }
        var env = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var pair in environment)
        {
            if (pair.Key is not ("SystemRoot" or "WINDIR" or "DOTNET_ROOT" or "DOTNET_ROOT_X64" or "LANG" or "LC_ALL")
                || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 2048 || pair.Value.Any(char.IsControl))
            { throw new InvalidDataException("The first stdio profile accepts only explicitly reviewed OS/runtime environment values."); }
            ConversationText.RequireNoCredentials(pair.Value);
            env.Add(pair.Key, pair.Value);
        }
        Executable = executable;
        Arguments = args;
        WorkingDirectory = workingDirectory;
        Environment = env.ToImmutable();
        Digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { Executable, Arguments, WorkingDirectory, Environment })));
    }

    public string Executable { get; }
    public ImmutableArray<string> Arguments { get; }
    public string WorkingDirectory { get; }
    public ImmutableSortedDictionary<string, string> Environment { get; }
    public string Digest { get; }
    public string NativeText => "Local stdio executable: " + Executable + "\nLiteral arguments: "
        + JsonSerializer.Serialize(Arguments) + "\nWorking directory: " + WorkingDirectory
        + "\nExplicit environment (no inheritance): " + JsonSerializer.Serialize(Environment)
        + "\nHost-private TEMP/TMP only. This process is NOT sandboxed; it may mutate, access the network or create descendants.";
    public override string ToString() => nameof(McpStdioEndpoint);

    private static void RequirePath(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length is < 4 or > 2048 || !char.IsAsciiLetter(value[0])
            || value[1] != ':' || value[2] != '\\' || value.Contains('/') || value.IndexOf(':', 2) >= 0
            || value.Any(char.IsControl) || value.Contains('%')
            || value[3..].Split('\\').Any(part => part.Length == 0 || part is "." or ".."
                || part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0))
        { throw new InvalidDataException("Stdio requires an exact canonical local drive path without expansion."); }
        ConversationText.RequireNoCredentials(value);
    }
}
