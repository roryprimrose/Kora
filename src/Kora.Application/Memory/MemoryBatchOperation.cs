namespace Kora.Application.Memory;

/// <summary>The only admitted selected-subset operations; never review, admit, use or scope promotion.</summary>
internal enum MemoryBatchOperation
{
    Disable,
    Forget,
}
