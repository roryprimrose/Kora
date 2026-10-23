using Kora.Core.Context;

namespace Kora.Application.Definitions;

/// <summary>Separate local read consent and complete disabled-copy confirmation.</summary>
public interface IProfileInstructionCopyReview
{
    /// <summary>Reviews the exact source, reader, limits and final freshness-read scope before any body read.</summary>
    Task<bool> ConfirmRead(LocalFileMetadata source, CancellationToken token);
    /// <summary>Reviews the complete original and new bytes, new identity, attribution and disabled destination.</summary>
    Task<bool> ConfirmCopy(ProfileInstructionCopy copy, CancellationToken token);
}
