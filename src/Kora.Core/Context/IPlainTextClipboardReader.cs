namespace Kora.Core.Context;

public interface IPlainTextClipboardReader
{
    Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken);
}
