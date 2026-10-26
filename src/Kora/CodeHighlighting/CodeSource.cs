using Kora.Core.Presentation.CodeHighlighting;

namespace Kora.CodeHighlighting;

internal sealed record CodeSource(string Text, CodeLanguage Language, int SourceStart = 0);
