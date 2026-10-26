using System.Text;

using Kora.Core.Customizations;

namespace Kora.Core.Tools;

/// <summary>A separate local-only metadata catalogue, never part of ordinary observation turns.</summary>
public static class CustomizationDiscoveryTools
{
    public const string List = "customizations.list";
    public const string Get = "customizations.get";
    public const string ResultReason = "reviewed-customization-metadata-lower-trust";
    public const int MaximumRecords = 6;
    public const int MaximumDescriptionUtf8Bytes = 256;
    public const int MaximumCumulativeResultUtf8Bytes = 8192;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static IReadOnlyList<CapabilityDescriptor> Descriptors { get; } = Array.AsReadOnly<CapabilityDescriptor>(
    [
        new(List, 1, CapabilityInputShape.Page, CapabilityOutputShape.CustomizationMetadata,
            CapabilityEffect.ReadOnlyObservation, CapabilityAvailability.Available,
            Array.AsReadOnly<CapabilityLane>([CapabilityLane.Execution]), new(1024, MaximumRecords, 4096)),
        new(Get, 1, CapabilityInputShape.MetadataReference, CapabilityOutputShape.CustomizationMetadata,
            CapabilityEffect.ReadOnlyObservation, CapabilityAvailability.Available,
            Array.AsReadOnly<CapabilityLane>([CapabilityLane.Execution]), new(1024, 1, 4096)),
    ]);

    public static CapabilityDescriptor? FindExact(string id) =>
        Descriptors.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public static bool IsReference(string value) =>
        value.Length == 32 && Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty
        && string.Equals(value, id.ToString("N"), StringComparison.Ordinal);

    public static bool IsSupported(CustomizationKind kind) =>
        kind is CustomizationKind.Skill or CustomizationKind.Agent or CustomizationKind.Instruction;

    public static bool IsValid(CustomizationMetadata? record)
    {
        if (record is null || !IsReference(record.Ref) || !IsSupported(record.Kind)
            || record.SourceCategory is not (CustomizationScope.Kora or CustomizationScope.User)
            || !record.Compatible || record.Availability != CapabilityAvailability.NotObserved
            || string.IsNullOrWhiteSpace(record.Name) || record.Name.Length > 128
            || string.IsNullOrWhiteSpace(record.Description))
        { return false; }
        try
        {
            Conversation.ConversationText.RequireNoCredentials(record.Name);
            Conversation.ConversationText.RequireNoCredentials(record.Description);
            return StrictUtf8.GetByteCount(record.Name) <= 128
                && StrictUtf8.GetByteCount(record.Description) <= MaximumDescriptionUtf8Bytes
                && !record.Name.Any(char.IsControl) && !record.Description.Any(char.IsControl);
        }
        catch (EncoderFallbackException) { return false; }
        catch (Conversation.ConversationCredentialException) { return false; }
    }

    public static bool Matches(CapabilityReply reply, string id, CapabilityInputResult input)
    {
        if (!string.Equals(reply.Reason, ResultReason, StringComparison.Ordinal)
            || reply.Customizations is not { Records: not null } page
            || page.TotalRecords is < 1 or > MaximumRecords || !page.Records.All(IsValid)
            || page.Records.Select(item => item.Ref).Distinct(StringComparer.Ordinal).Count() != page.Records.Count
            || reply.Capabilities is not null || reply.Descriptor is not null || reply.Version is not null
            || reply.Readiness is not null || reply.Runtimes is not null || reply.Runtime is not null
            || reply.WebPage is not null || reply.Mcp is not null || reply.Sessions is not null)
        { return false; }
        return string.Equals(id, Get, StringComparison.Ordinal)
            ? page.TotalRecords == 1 && page.NextOffset is null && page.Records.Count == 1
                && string.Equals(page.Records[0].Ref, input.Id!.Id, StringComparison.Ordinal)
            : CapabilityPagePolicy.Resolve(input.Page!, page.TotalRecords) is { } resolved
                && page.Records.Count == resolved.Count && page.NextOffset == resolved.NextOffset;
    }
}
