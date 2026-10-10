using Kora.Core.Auditing;
using Kora.Core.Context;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

/// <summary>Session capture uses the same verified native broker, never a second path read or preview consent.</summary>
public sealed class LocalSessionFileAttach(
    ILocalFileInspector inspector, ISecurityAuditLog audit, TimeProvider time, ILogger<LocalFilePreview> logger)
    : IAsyncDisposable
{
    private readonly LocalFilePreview capture = new(inspector, audit, time, logger);
    public LocalFileReview? Review => capture.Review;
    public bool IsQuiescent => capture.IsQuiescent;
    public void Revoke() => capture.Clear();
    public Task WaitForQuiescence() => capture.WaitForQuiescenceAsync();
    public Task<LocalFileOutcome> Select(IUserFilePicker picker, Func<bool> admitted, CancellationToken token) =>
        capture.SelectAsync(picker, admitted, token);
    public Task<LocalFileOutcome> Execute(Guid exactReview, Func<bool> admitted,
        Func<LocalFileRevision, ReadOnlyMemory<byte>, CancellationToken, Task> persist, CancellationToken token) =>
        capture.ConfirmAttachment(exactReview, admitted, persist, token);
    public async Task Clear()
    {
        capture.Clear();
        await capture.WaitForQuiescenceAsync().ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() => capture.DisposeAsync();
}
