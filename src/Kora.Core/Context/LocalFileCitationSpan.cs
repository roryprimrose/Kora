namespace Kora.Core.Context;

public sealed record LocalFileCitationSpan(LocalFileRevision File, LocalFileCitation Citation, string Text);
