using System.Text.Json;

namespace Kora.Core.Context;

/// <summary>Content-free syntax evidence only; no semantic, uniqueness or source-use authority.</summary>
public sealed record ClipboardJsonSyntaxResult(
    ClipboardJsonSyntaxOutcome Outcome, JsonValueKind? RootKind = null, long? LineNumber = null, long? BytePositionInLine = null);
