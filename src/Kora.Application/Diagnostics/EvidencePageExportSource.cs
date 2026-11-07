using Kora.Core.Diagnostics;

namespace Kora.Application.Diagnostics;

public sealed class EvidencePageExportSource(EvidencePageExportSnapshot snapshot, Func<CancellationToken, Task> validate)
{
    public EvidencePageExportSnapshot Snapshot { get; } = snapshot;
    public Task ValidateAsync(CancellationToken token) => validate(token);
}
