using Kora.Core.Hosting;

namespace Kora.Core.Tools;

/// <summary>A separate exact-source catalogue. It never widens ordinary read-only turns.</summary>
public static class SessionDiscoveryTool
{
    public const string Id = "sessions.list";
    public const string ResultReason = "reviewed-active-metadata-lower-trust";
    public const int MaximumRecords = 6;
    public static CapabilityDescriptor Descriptor { get; } = new(Id, 1, CapabilityInputShape.Page,
        CapabilityOutputShape.Sessions, CapabilityEffect.ReadOnlyObservation, CapabilityAvailability.Available,
        Array.AsReadOnly<CapabilityLane>([CapabilityLane.Execution]),
        new(1024, MaximumRecords, 4096));
    public static IReadOnlyList<CapabilityDescriptor> Descriptors { get; } =
        Array.AsReadOnly<CapabilityDescriptor>([Descriptor]);

    public static bool IsValidRecord(SessionDescriptor? record)
    {
        if (record is null || !Guid.TryParseExact(record.SessionId, "D", out var id) || id == Guid.Empty
            || !string.Equals(record.SessionId, id.ToString("D"), StringComparison.Ordinal))
        { return false; }
        if (record.Name is null) { return true; }
        try { _ = new SessionName(record.Name); return true; }
        catch (InvalidDataException) { return false; }
    }

    public static (int Count, int? NextOffset)? Resolve(CapabilityPageInput input, int total) =>
        total == 0 && input.Offset == 0 ? (0, null) : CapabilityPagePolicy.Resolve(input, total);

    public static bool Matches(CapabilityReply reply, CapabilityPageInput input) =>
        string.Equals(reply.Reason, ResultReason, StringComparison.Ordinal) && reply.Sessions is { Records: not null } page
        && page.TotalRecords is >= 0 and <= MaximumRecords
        && Resolve(input, page.TotalRecords) is { } resolved
        && page.Records.Count == resolved.Count && page.NextOffset == resolved.NextOffset
        && page.Records.All(IsValidRecord)
        && page.Records.Select(record => record.SessionId).Distinct(StringComparer.Ordinal).Count() == page.Records.Count
        && reply.Capabilities is null && reply.Descriptor is null && reply.Version is null
        && reply.Readiness is null && reply.Runtimes is null && reply.Runtime is null
        && reply.WebPage is null && reply.Mcp is null;
}
