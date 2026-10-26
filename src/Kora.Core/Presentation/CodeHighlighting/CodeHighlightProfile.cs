using System.Collections.Frozen;

namespace Kora.Core.Presentation.CodeHighlighting;

public static class CodeHighlightProfile
{
    public const string Name = "offline-code-v1";
    public const int MaximumUtf8Bytes = 32 * 1024;
    public const int MaximumLines = 1024;
    public const int MaximumTokens = 2048;
    public const int MaximumNesting = 64;
    public static TimeSpan MaximumDuration => TimeSpan.FromMilliseconds(100);

    private static readonly FrozenDictionary<string, CodeLanguage> Aliases =
        new Dictionary<string, CodeLanguage>(StringComparer.OrdinalIgnoreCase)
        {
            ["powershell"] = CodeLanguage.PowerShell, ["ps1"] = CodeLanguage.PowerShell,
            ["csharp"] = CodeLanguage.CSharp, ["cs"] = CodeLanguage.CSharp,
            ["json"] = CodeLanguage.Json,
            ["yaml"] = CodeLanguage.Yaml, ["yml"] = CodeLanguage.Yaml,
            ["xml"] = CodeLanguage.Xml, ["xaml"] = CodeLanguage.Xml,
            ["markdown"] = CodeLanguage.Markdown, ["md"] = CodeLanguage.Markdown,
            ["sql"] = CodeLanguage.Sql,
            ["javascript"] = CodeLanguage.JavaScript, ["js"] = CodeLanguage.JavaScript,
            ["typescript"] = CodeLanguage.TypeScript, ["ts"] = CodeLanguage.TypeScript,
            ["html"] = CodeLanguage.Html, ["htm"] = CodeLanguage.Html,
            ["css"] = CodeLanguage.Css,
            ["shell"] = CodeLanguage.Shell, ["sh"] = CodeLanguage.Shell, ["bash"] = CodeLanguage.Shell,
            ["batch"] = CodeLanguage.Batch, ["bat"] = CodeLanguage.Batch, ["cmd"] = CodeLanguage.Batch,
            ["ini"] = CodeLanguage.Ini, ["properties"] = CodeLanguage.Ini,
            ["diff"] = CodeLanguage.Diff, ["patch"] = CodeLanguage.Diff,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // File names and host language are trusted snapshot metadata, never Markdown-supplied paths.
    public static CodeLanguage Resolve(
        string? hostLanguage = null, string? snapshotFileName = null, string? fenceLanguage = null)
    {
        if (hostLanguage is not null) { return ResolveAlias(hostLanguage); }
        if (snapshotFileName is not null)
        {
            var name = snapshotFileName.AsSpan();
            var separator = name.LastIndexOfAny('\\', '/');
            name = name[(separator + 1)..];
            if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) { return CodeLanguage.Shell; }
            if (name.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)) { return CodeLanguage.Ini; }
            var dot = name.LastIndexOf('.');
            return dot < 0 ? CodeLanguage.PlainText : ResolveAlias(name[(dot + 1)..].ToString());
        }
        return ResolveAlias(fenceLanguage);
    }

    private static CodeLanguage ResolveAlias(string? value) =>
        value is not null && Aliases.TryGetValue(value.Trim(), out var language) ? language : CodeLanguage.PlainText;
}
