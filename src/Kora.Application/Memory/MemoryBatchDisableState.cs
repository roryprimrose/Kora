namespace Kora.Application.Memory;

/// <summary>Truthful per-record outcomes; Succeeded is the individual committed transition, not batch atomicity.</summary>
internal enum MemoryBatchDisableState
{
    /// <summary>The individual action returned its committed success.</summary>
    Succeeded,
    /// <summary>The exact reviewed record changed or its transition conflicted.</summary>
    Conflict,
    /// <summary>Admission or a confirmed commit result is unavailable; inspect durable state manually.</summary>
    Unavailable,
    /// <summary>Cancellation preceded the disable call, or the authoritative action returned Cancelled.</summary>
    Cancelled,
    /// <summary>No disable call was made for this record.</summary>
    NotAttempted,
}
