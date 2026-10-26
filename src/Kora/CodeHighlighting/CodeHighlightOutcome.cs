namespace Kora.CodeHighlighting;

internal enum CodeHighlightOutcome { Highlighted, Unsupported, ViewLimit, ByteLimit, LineLimit, TokenLimit, NestingLimit, TimeLimit, InvalidUnicode, Cancelled, Failed }
