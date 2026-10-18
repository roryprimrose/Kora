namespace Kora.Application.Memory;

internal sealed class MemoryBatchDisableConflictException()
    : InvalidOperationException("The exact selected memory revision changed. List and review again.")
{
}
