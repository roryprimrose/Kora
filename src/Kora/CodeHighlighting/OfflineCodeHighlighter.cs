using System.Text;

using Kora.Core.Presentation.CodeHighlighting;

namespace Kora.CodeHighlighting;

// Deliberately lexical, not a compiler or validator. These rules are bundled application code.
internal sealed class OfflineCodeHighlighter(TimeProvider? timeProvider = null)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    internal CodeProjection Highlight(CodeSource source, CancellationToken cancellationToken = default)
    {
        var started = clock.GetTimestamp();
        CodeProjection Fallback(CodeHighlightOutcome outcome) => new(source, outcome, []);
        if (cancellationToken.IsCancellationRequested) { return Fallback(CodeHighlightOutcome.Cancelled); }
        if (source.Language == CodeLanguage.PlainText) { return Fallback(CodeHighlightOutcome.Unsupported); }
        if (source.Text.Length > CodeHighlightProfile.MaximumUtf8Bytes) { return Fallback(CodeHighlightOutcome.ByteLimit); }
        try
        {
            if (Utf8.GetByteCount(source.Text) > CodeHighlightProfile.MaximumUtf8Bytes)
            { return Fallback(CodeHighlightOutcome.ByteLimit); }
        }
        catch (EncoderFallbackException) { return Fallback(CodeHighlightOutcome.InvalidUnicode); }

        var tokens = new List<CodeToken>();
        var text = source.Text;
        var lines = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n')) { continue; }
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') { index++; }
            if (++lines > CodeHighlightProfile.MaximumLines) { return Fallback(CodeHighlightOutcome.LineLimit); }
        }
        var nesting = 0;
        for (var index = 0; index < text.Length;)
        {
            if (cancellationToken.IsCancellationRequested) { return Fallback(CodeHighlightOutcome.Cancelled); }
            if (clock.GetElapsedTime(started) >= CodeHighlightProfile.MaximumDuration)
            { return Fallback(CodeHighlightOutcome.TimeLimit); }
            if (text[index] is '\r' or '\n')
            {
                if (text[index++] == '\r' && index < text.Length && text[index] == '\n') { index++; }
                continue;
            }
            if (char.IsWhiteSpace(text[index])) { index++; continue; }
            var start = index;
            CodeTokenKind? kind = null;
            var current = text[index++];
            if (source.Language == CodeLanguage.Diff && (start == 0 || text[start - 1] is '\r' or '\n')
                && current is '+' or '-')
            {
                kind = current == '+' ? CodeTokenKind.Added : CodeTokenKind.Removed;
                while (index < text.Length && text[index] is not ('\r' or '\n')) { index++; }
            }
            else if (BlockCommentEnd(text.AsSpan(start), source.Language) is { } terminator)
            {
                kind = CodeTokenKind.Comment;
                var end = text.AsSpan(index).IndexOf(terminator, StringComparison.Ordinal);
                index = end < 0 ? text.Length : index + end + terminator.Length;
            }
            else if (IsLineComment(text, start, source.Language))
            {
                kind = CodeTokenKind.Comment;
                while (index < text.Length && text[index] is not ('\r' or '\n')) { index++; }
            }
            else if (current is '"' or '\'' or '`')
            {
                kind = CodeTokenKind.String;
                while (index < text.Length && text[index] is not ('\r' or '\n'))
                {
                    var character = text[index++];
                    if (character == '\\' && index < text.Length && text[index] is not ('\r' or '\n')) { index++; }
                    else if (character == current) { break; }
                }
            }
            else if (char.IsDigit(current))
            {
                kind = CodeTokenKind.Number;
                while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] is '.' or '_')) { index++; }
            }
            else if (char.IsLetter(current) || current is '_' or '$' or '@')
            {
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '-' or '$'
                    || text[index] >= 128 && !char.IsWhiteSpace(text[index]))) { index++; }
                if (IsKeyword(text.AsSpan(start, index - start), source.Language)) { kind = CodeTokenKind.Keyword; }
            }
            else if (current >= 128)
            {
                if (char.IsHighSurrogate(current) && index < text.Length && char.IsLowSurrogate(text[index])) { index++; }
            }
            else
            {
                kind = CodeTokenKind.Punctuation;
                if (current is '(' or '[' or '{')
                {
                    if (++nesting > CodeHighlightProfile.MaximumNesting) { return Fallback(CodeHighlightOutcome.NestingLimit); }
                }
                else if (current is ')' or ']' or '}') { nesting = Math.Max(0, nesting - 1); }
            }
            if (kind is { } tokenKind)
            {
                if (tokens.Count == CodeHighlightProfile.MaximumTokens) { return Fallback(CodeHighlightOutcome.TokenLimit); }
                tokens.Add(new(start, index - start, tokenKind));
            }
        }
        if (cancellationToken.IsCancellationRequested) { return Fallback(CodeHighlightOutcome.Cancelled); }
        if (clock.GetElapsedTime(started) >= CodeHighlightProfile.MaximumDuration)
        { return Fallback(CodeHighlightOutcome.TimeLimit); }
        return new(source, CodeHighlightOutcome.Highlighted, tokens.AsReadOnly());
    }

    private static string? BlockCommentEnd(ReadOnlySpan<char> rest, CodeLanguage language) => language switch
    {
        CodeLanguage.CSharp or CodeLanguage.JavaScript or CodeLanguage.TypeScript or CodeLanguage.Css or CodeLanguage.Sql
            when rest.StartsWith("/*", StringComparison.Ordinal) => "*/",
        CodeLanguage.PowerShell when rest.StartsWith("<#", StringComparison.Ordinal) => "#>",
        CodeLanguage.Xml or CodeLanguage.Html when rest.StartsWith("<!--", StringComparison.Ordinal) => "-->",
        _ => null,
    };

    private static bool IsLineComment(string text, int index, CodeLanguage language)
    {
        var rest = text.AsSpan(index);
        return language switch
        {
            CodeLanguage.PowerShell or CodeLanguage.Yaml or CodeLanguage.Shell => rest[0] == '#',
            CodeLanguage.Ini => rest[0] is '#' or ';',
            CodeLanguage.Sql => rest.StartsWith("--", StringComparison.Ordinal),
            CodeLanguage.CSharp or CodeLanguage.JavaScript or CodeLanguage.TypeScript or CodeLanguage.Css =>
                rest.StartsWith("//", StringComparison.Ordinal),
            CodeLanguage.Batch => rest.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("::", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool IsKeyword(ReadOnlySpan<char> word, CodeLanguage language)
    {
        // Language-specific keyword sets keep prose and JSON property names out of keyword styling.
        var keywords = language switch
        {
            CodeLanguage.CSharp => "class namespace using public private internal static sealed void var new return if else async await true false null",
            CodeLanguage.JavaScript or CodeLanguage.TypeScript => "const let var function return if else class import export async await true false null undefined interface type",
            CodeLanguage.PowerShell => "function param begin process end if else foreach return write-output get-item $true $false $null",
            CodeLanguage.Json or CodeLanguage.Yaml => "true false null",
            CodeLanguage.Sql => "select from where join on insert into update delete create table null as and or",
            CodeLanguage.Shell => "if then else fi for do done function echo exit",
            CodeLanguage.Batch => "echo set if else for do goto exit",
            CodeLanguage.Css => "color background display block flex grid font margin padding",
            _ => string.Empty,
        };
        foreach (var candidate in keywords.AsSpan().Split(' '))
        {
            if (word.Equals(keywords.AsSpan()[candidate], StringComparison.OrdinalIgnoreCase)) { return true; }
        }
        return false;
    }
}
