using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Core.Tools;

/// <summary>One strict input policy for both complete model proposals and the host registry.</summary>
public static class CapabilityInputPolicy
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static CapabilityInputResult Read(CapabilityDescriptor descriptor, string input)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(input);
        JsonDocument document;
        bool duplicates;
        try { document = ModelToolProtocol.ReadJson(EncodingStrict(input), descriptor.Limits.MaximumInputUtf8Bytes,
            out duplicates, maximumDepth: 4); }
        catch (InvalidDataException)
        {
            return new(Encoding.UTF8.GetByteCount(input) > descriptor.Limits.MaximumInputUtf8Bytes
                ? "input-limit-exceeded" : "invalid-input");
        }
        using (document)
        {
            if (duplicates) { return new("duplicate-input-field"); }
            if (document.RootElement.ValueKind != JsonValueKind.Object) { return new("invalid-input"); }
            var fields = document.RootElement.EnumerateObject().ToArray();
            if (descriptor.Input == CapabilityInputShape.MetadataReference)
            {
                return fields.Length == 1 && string.Equals(fields[0].Name, "ref", StringComparison.Ordinal)
                    && fields[0].Value.ValueKind == JsonValueKind.String
                    && CustomizationDiscoveryTools.IsReference(fields[0].Value.GetString()!)
                    ? new(null, Id: new(fields[0].Value.GetString()!)) : new("invalid-reviewed-reference");
            }
            if (descriptor.Input == CapabilityInputShape.WebPageAddress)
            {
                if (fields.Length != 1 || !string.Equals(fields[0].Name, "address", StringComparison.Ordinal)
                    || fields[0].Value.ValueKind != JsonValueKind.String
                    || !Uri.TryCreate(fields[0].Value.GetString(), UriKind.Absolute, out var address))
                { return new("invalid-web-page-address"); }
                try { _ = Network.WebPageAccessBinding.DestinationDigest(address); }
                catch (ArgumentException) { return new("invalid-web-page-address"); }
                return new(null, WebPageAddress: address);
            }
            if (descriptor.Input == CapabilityInputShape.None)
            { return new(fields.Length == 0 ? null : "unknown-input-field"); }
            if (descriptor.Input == CapabilityInputShape.Id)
            {
                if (fields.Length != 1 || !string.Equals(fields[0].Name, "id", StringComparison.Ordinal)
                    || fields[0].Value.ValueKind != JsonValueKind.String)
                { return new("invalid-id-input"); }
                var id = new CapabilityIdInput(fields[0].Value.GetString()!);
                var valid = string.Equals(descriptor.Id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal)
                    ? ReadOnlyCapabilityCatalog.FindExact(id.Id) is not null
                    : string.Equals(id.Id, ReadOnlyCapabilityCatalog.LocalRuntimeId, StringComparison.Ordinal);
                return valid ? new(null, Id: id) : new(string.Equals(descriptor.Id, ReadOnlyCapabilityCatalog.Get,
                    StringComparison.Ordinal) ? "unknown-capability" : "unknown-runtime");
            }
            if (descriptor.Input != CapabilityInputShape.Page || fields.Any(field => field.Name is not ("offset" or "count")
                || field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out _)))
            { return new("invalid-page-input"); }
            var page = new CapabilityPageInput(
                document.RootElement.TryGetProperty("offset", out var offset) ? offset.GetInt32() : 0,
                document.RootElement.TryGetProperty("count", out var count) ? count.GetInt32() : descriptor.Limits.MaximumRecords);
            return page.Offset < 0 || page.Count < 1 || page.Count > descriptor.Limits.MaximumRecords
                ? new("page-out-of-range") : new(null, Page: page);
        }
    }

    private static byte[] EncodingStrict(string input)
    {
        try { return StrictUtf8.GetBytes(input); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Malformed input Unicode.", exception); }
    }
}
