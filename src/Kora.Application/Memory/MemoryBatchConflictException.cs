namespace Kora.Application.Memory;

internal sealed class MemoryBatchConflictException()
    : InvalidOperationException("The exact selected memory revision changed. List and review again.")
{
}
