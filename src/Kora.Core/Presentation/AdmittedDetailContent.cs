using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kora.Core.Hosting;

namespace Kora.Core.Presentation;

// Only a trusted host resolver constructs this snapshot. No wire/model deserializer
// or viewer identifier can establish session authority.
public sealed class AdmittedDetailContent
{
    private static readonly ActivitySource ActivitySource =
        new("Kora.Core", typeof(AdmittedDetailContent).Assembly.GetName().Version!.ToString());

    public AdmittedDetailContent(
        DetailContentReference reference,
        DetailContentKind kind,
        DetailContentOrigin origin,
        DetailSensitivity sensitivity,
        string title,
        string provenance,
        string source,
        DetailSessionSource? sessionSource = null,
        HostId<SessionIdentity>? historySession = null)
    {
        using var admission = ActivitySource.StartActivity("presentation.admit");
        admission?.SetStatus(ActivityStatusCode.Error);
        reference.Validate();
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(origin) || !Enum.IsDefined(sensitivity))
        {
            throw new InvalidDataException("The native detail profile does not support this content classification.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance);
        if (title.Length > 256 || provenance.Length > 256)
        {
            throw new InvalidDataException("Native detail chrome exceeds its label limit.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var encoding = new UTF8Encoding(false, true);
        var byteCount = encoding.GetByteCount(source);
        if (byteCount > NativeDetailProfile.MaximumUtf8Bytes)
        {
            throw new InvalidDataException("Content exceeds the native-text-v1 256 KiB UTF-8 limit; no text was truncated.");
        }
        if ((origin == DetailContentOrigin.FinalizedResponse) != (sessionSource is not null))
        {
            throw new InvalidDataException("Finalized responses require host session/request/task identity; embedded documents must not claim session authority.");
        }
        sessionSource?.Validate();
        if ((origin == DetailContentOrigin.SessionHistory) != (historySession is not null))
        {
            throw new InvalidDataException("History details require their exact retained session ownership.");
        }
        historySession?.Validate();
        Reference = reference;
        Kind = kind;
        Origin = origin;
        Sensitivity = sensitivity;
        Title = title;
        Provenance = provenance;
        Source = source;
        SessionSource = sessionSource;
        HistorySession = historySession;
        Utf8Bytes = byteCount;
        Digest = Convert.ToHexString(SHA256.HashData(encoding.GetBytes(source)));
        admission?.SetTag("kora.item.id", reference.ItemId.Value);
        admission?.SetTag("kora.item.revision", reference.Revision);
        admission?.SetTag("kora.presentation.profile", NativeDetailProfile.Name);
        admission?.SetTag("kora.presentation.origin", origin.ToString());
        admission?.SetTag("kora.presentation.bytes", Utf8Bytes);
        admission?.SetStatus(ActivityStatusCode.Ok);
    }

    public DetailContentReference Reference { get; }
    public DetailContentKind Kind { get; }
    public DetailContentOrigin Origin { get; }
    public DetailSensitivity Sensitivity { get; }
    public string Title { get; }
    public string Provenance { get; }
    public string Source { get; }
    public DetailSessionSource? SessionSource { get; }
    public HostId<SessionIdentity>? HistorySession { get; }
    public int Utf8Bytes { get; }
    public string Digest { get; }

    public bool IsSameSnapshot(AdmittedDetailContent other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Reference == other.Reference
            && Kind == other.Kind && Origin == other.Origin && Sensitivity == other.Sensitivity
            && string.Equals(Title, other.Title, StringComparison.Ordinal)
            && string.Equals(Provenance, other.Provenance, StringComparison.Ordinal)
            && string.Equals(Source, other.Source, StringComparison.Ordinal)
            && SessionSource == other.SessionSource && Nullable.Equals(HistorySession, other.HistorySession);
    }
}
