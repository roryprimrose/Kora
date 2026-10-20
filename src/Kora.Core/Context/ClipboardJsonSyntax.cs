using System.Text.Json;

namespace Kora.Core.Context;

public static class ClipboardJsonSyntax
{
    public const int MaximumDepth = 64;

    public static ClipboardJsonSyntaxResult Validate(ClipboardSnapshot snapshot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        token.ThrowIfCancellationRequested();
        var bytes = snapshot.GetUtf8Bytes();
        // One extra reader level identifies the explicit refusal without parsing exception messages.
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            MaxDepth = MaximumDepth + 1,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            AllowMultipleValues = false,
        });
        JsonValueKind? root = null;
        try
        {
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (reader.CurrentDepth == MaximumDepth
                    && reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
                {
                    var (line, position) = Position(bytes.AsSpan(0, checked((int)reader.TokenStartIndex)));
                    return new(ClipboardJsonSyntaxOutcome.DepthLimit, LineNumber: line, BytePositionInLine: position);
                }
                root ??= reader.TokenType switch
                {
                    JsonTokenType.StartObject => JsonValueKind.Object,
                    JsonTokenType.StartArray => JsonValueKind.Array,
                    JsonTokenType.String => JsonValueKind.String,
                    JsonTokenType.Number => JsonValueKind.Number,
                    JsonTokenType.True => JsonValueKind.True,
                    JsonTokenType.False => JsonValueKind.False,
                    _ => JsonValueKind.Null,
                };
            }
        }
        catch (JsonException exception)
        {
            return new(ClipboardJsonSyntaxOutcome.InvalidSyntax,
                LineNumber: exception.LineNumber, BytePositionInLine: exception.BytePositionInLine);
        }
        token.ThrowIfCancellationRequested();
        // The final-block reader rejects empty/whitespace input before successful completion.
        return new(ClipboardJsonSyntaxOutcome.Valid, root!.Value);
    }

    private static (long Line, long Position) Position(ReadOnlySpan<byte> bytes)
    {
        long line = 0;
        long position = 0;
        foreach (var value in bytes)
        {
            if (value == (byte)'\n') { line++; position = 0; }
            else { position++; }
        }
        return (line, position);
    }
}
