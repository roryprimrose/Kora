using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kora.Core.Diagnostics;

public sealed class EvidencePageExportSnapshot
{
    public const string ExclusionDisclosure = "One bounded evidence page only. The live continuation cursor is omitted (Cursor is null). No other page, session/conversation content or reconstructed missing content is exported. Record, source, gap and daily-file provenance remain exactly as reviewed.";
    private static readonly JsonSerializerOptions Format = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly byte[] bytes;

    public EvidencePageExportSnapshot(EvidencePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Records.Count > EvidencePage.MaximumRecords || Serialize(page).Length > EvidencePage.MaximumBytes)
        { throw new InvalidDataException("The complete evidence page exceeds its admitted export bound."); }
        bytes = Serialize(page with { Cursor = null });
    }

    public string Text => Encoding.UTF8.GetString(bytes);
    public byte[] CopyBytes() => bytes.ToArray();
    public static byte[] Serialize(EvidencePage page) => JsonSerializer.SerializeToUtf8Bytes(page, Format);
}
