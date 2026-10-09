using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kora.Core.Interaction;

/// <summary>Bounded suppression metadata, not durable source or permission authority.</summary>
public sealed record LocalEventBrokerState(
    int Schema, DateTimeOffset HighWatermark, IReadOnlyList<LocalEventReceipt> Receipts,
    IReadOnlyList<LocalEventCategoryBudget> Budgets)
{
    public const int MaximumReceipts = 64;
    public const int MaximumBytes = 65536;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);
    public static readonly TimeSpan Spacing = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Deferral = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8,
    };

    public static LocalEventBrokerState Empty(DateTimeOffset now) => new(1, now, [], []);
    public static int Limit(LocalEventCategory category) => category switch
    {
        LocalEventCategory.Work => 3,
        LocalEventCategory.Failure or LocalEventCategory.Attention => 2,
        _ => 1,
    };

    public LocalEventReason Reason(LocalEventReceipt receipt, DateTimeOffset now)
    {
        if (now < HighWatermark) { return LocalEventReason.ClockRollback; }
        if (now >= receipt.Event.ExpiresAt) { return LocalEventReason.Expired; }
        if (receipt.Disposition == LocalEventDisposition.Dismissed) { return LocalEventReason.Dismissed; }
        if (receipt.Disposition == LocalEventDisposition.Presented) { return LocalEventReason.PresentedNoReplay; }
        if (receipt.DeferredUntil > now) { return LocalEventReason.Deferred; }
        var budget = Budgets.SingleOrDefault(item => item.Category == receipt.Event.Category);
        return budget is not null && now - budget.WindowStart < Window
            && (budget.Count >= Limit(budget.Category) || now - budget.LastPresentation!.Value < Spacing)
            ? LocalEventReason.CategoryLimit : LocalEventReason.Eligible;
    }

    public void Validate()
    {
        if (Schema != 1 || HighWatermark.Offset != TimeSpan.Zero || Receipts is null || Budgets is null
            || Receipts.Count > MaximumReceipts || Budgets.Count > 4
            || Receipts.Select(item => item.Event.Id).Distinct().Count() != Receipts.Count
            || Budgets.Select(item => item.Category).Distinct().Count() != Budgets.Count)
        { throw new InvalidDataException("The local event state schema, chronology or bounds are invalid."); }
        foreach (var receipt in Receipts)
        {
            receipt.Event.Validate();
            if (!Enum.IsDefined(receipt.Disposition) || receipt.Event.ObservedAt > HighWatermark
                || receipt.ChangedAt.Offset != TimeSpan.Zero || receipt.ChangedAt < receipt.Event.ObservedAt || receipt.ChangedAt > HighWatermark
                || (receipt.Disposition == LocalEventDisposition.Deferred
                    ? receipt.DeferredUntil is not { } deferred || deferred.Offset != TimeSpan.Zero
                        || deferred <= receipt.ChangedAt || deferred > receipt.Event.ExpiresAt
                        || deferred > receipt.ChangedAt + Deferral
                    : receipt.DeferredUntil is not null))
            { throw new InvalidDataException("The local event receipt or deferral is invalid."); }
        }
        foreach (var budget in Budgets)
        {
            if (!Enum.IsDefined(budget.Category) || budget.WindowStart.Offset != TimeSpan.Zero
                || budget.WindowStart > HighWatermark || budget.Count < 1 || budget.Count > Limit(budget.Category)
                || budget.LastPresentation is not { } presented || presented.Offset != TimeSpan.Zero
                || presented < budget.WindowStart || presented > HighWatermark)
            { throw new InvalidDataException("The local event fatigue budget is invalid."); }
        }
    }

    public static string Serialize(LocalEventBrokerState state)
    {
        state.Validate();
        return JsonSerializer.Serialize(state, Options);
    }

    public static LocalEventBrokerState Deserialize(string text)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
            { throw new InvalidDataException("The broker state exceeds its byte bound."); }
            var state = JsonSerializer.Deserialize<LocalEventBrokerState>(text, Options)
                ?? throw new InvalidDataException("The broker state is missing.");
            state.Validate();
            // Reject duplicate properties, missing/default fields and obsolete representations.
            if (!string.Equals(Serialize(state), text, StringComparison.Ordinal))
            { throw new InvalidDataException("The broker state is noncanonical or obsolete."); }
            return state;
        }
        catch (JsonException exception) { throw new InvalidDataException("The broker state is malformed.", exception); }
        catch (NullReferenceException exception) { throw new InvalidDataException("The broker state has missing records.", exception); }
    }
}
