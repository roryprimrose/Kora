using System.Collections.Immutable;
using System.Text.Json;

namespace Kora.Core.Authorization;

/// <summary>A complete immutable exact selection, not a query or execution authority.</summary>
public sealed record ExactGrantBatch(Guid PreviewId, ImmutableArray<ExactGrantInspection> Records)
{
    public void Validate()
    {
        if (PreviewId == Guid.Empty || Records.IsDefaultOrEmpty || Records.Length > ExactGrantPage.MaximumRecords)
        {
            throw new InvalidDataException("An exact batch requires 1–50 complete selected records.");
        }
        var ids = new HashSet<Guid>();
        string? identity = null;
        foreach (var item in Records)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.StoreIdentity))
            {
                throw new InvalidDataException("An exact batch has missing store provenance.");
            }
            item.Grant.Validate();
            identity ??= item.StoreIdentity;
            if (!string.Equals(identity, item.StoreIdentity, StringComparison.Ordinal)
                || !ids.Add(item.Grant.Id.Value) || item.Grant.Status != OperationGrantStatus.Active)
            {
                throw new InvalidDataException("An exact batch requires unique Active records from one current store.");
            }
        }
        if (JsonSerializer.SerializeToUtf8Bytes(this).Length > ExactGrantPage.MaximumBytes)
        {
            throw new InvalidDataException("The complete exact batch exceeds 64 KiB; reduce the selection and inspect again. No truncation is permitted.");
        }
    }
}