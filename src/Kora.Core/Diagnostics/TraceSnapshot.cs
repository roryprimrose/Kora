using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record TraceSnapshot(
    [property: JsonRequired] string TraceId,
    [property: JsonRequired] string SpanId,
    [property: JsonRequired] string? ParentSpanId,
    [property: JsonRequired] ActivityTraceFlags Flags,
    [property: JsonRequired] string Source,
    [property: JsonRequired] string? SourceVersion,
    [property: JsonRequired] string Name,
    [property: JsonRequired] ActivityKind Kind)
{
    public const int MaximumMetadataLength = 128;

    public void Validate()
    {
        if (!IsValidW3CId(TraceId, 32) || !IsValidW3CId(SpanId, 16)
            || ParentSpanId is not null && !IsValidW3CId(ParentSpanId, 16)
            || !Enum.IsDefined(Kind) || !Enum.IsDefined(Flags)
            || Source is not ("Kora.Core" or "Kora.Application" or "Kora.Windows" or "Kora.Desktop")
            || SourceVersion is not null && !IsValidMetadataText(SourceVersion)
            || !IsValidMetadataText(Name))
        {
            throw new InvalidDataException("The retained activity trace metadata is invalid.");
        }
    }

    public static bool IsValidMetadataText(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumMetadataLength
            || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]) || value.Any(char.IsControl))
        {
            return false;
        }
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException) { return false; }
        return true;
    }

    public static bool IsValidW3CId(string? value, int length) =>
        value is not null && value.Length == length && value.Any(character => character != '0')
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static TraceSnapshot? Capture(Activity? activity) => activity is null ? null :
        new(activity.TraceId.ToHexString(), activity.SpanId.ToHexString(),
            activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
            activity.ActivityTraceFlags, activity.Source.Name, activity.Source.Version,
            activity.OperationName, activity.Kind);
}
