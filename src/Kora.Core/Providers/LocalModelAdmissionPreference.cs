using System.Text;
using System.Text.Json;

namespace Kora.Core.Providers;

/// <summary>Retained reviewed evidence choice only; never a reusable process/session/qualification capability.</summary>
public sealed record LocalModelAdmissionPreference(string EvidenceDigest, Guid ProfileId, ProviderAccountId Account,
    DateTimeOffset ReviewedAt, DateTimeOffset ExpiresAt)
{
    public const int MaximumBytes = 2048;

    public void Validate()
    {
        if (!LocalModelQualificationEvidence.IsHash(EvidenceDigest, 64) || ProfileId == Guid.Empty || Account is null
            || ReviewedAt == DateTimeOffset.MinValue || ExpiresAt <= ReviewedAt)
        { throw new InvalidDataException("The saved local model admission choice is malformed; review it explicitly again."); }
    }

    public string Serialize()
    {
        Validate();
        return JsonSerializer.Serialize(new { Format = 1, EvidenceDigest, ProfileId, Account = Account.Value, ReviewedAt, ExpiresAt });
    }

    public static LocalModelAdmissionPreference Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        { throw new InvalidDataException("The saved local model admission choice exceeds its bound."); }
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            LocalModelQualificationEvidence.Fields(root, ["Format", "EvidenceDigest", "ProfileId", "Account", "ReviewedAt", "ExpiresAt"]);
            if (root.GetProperty("Format").GetInt32() != 1) { throw new InvalidDataException("Unknown local model admission format."); }
            var result = new LocalModelAdmissionPreference(root.GetProperty(nameof(EvidenceDigest)).GetString()!,
                root.GetProperty(nameof(ProfileId)).GetGuid(), new(root.GetProperty(nameof(Account)).GetString()!),
                root.GetProperty(nameof(ReviewedAt)).GetDateTimeOffset(), root.GetProperty(nameof(ExpiresAt)).GetDateTimeOffset());
            result.Validate();
            return result;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("The saved local model admission choice is malformed.", exception); }
    }
}