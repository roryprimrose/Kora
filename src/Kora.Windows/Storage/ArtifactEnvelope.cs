using System.Buffers.Binary;
using System.Security.Cryptography;

using Kora.Core.Hosting;

namespace Kora.Windows.Storage;

internal static class ArtifactEnvelope
{
    internal const int MaximumPlaintextBytes = 4 * 1024 * 1024;
    internal const int HeaderBytes = 92;
    internal const int TagBytes = 16;
    internal const int MaximumEnvelopeBytes = MaximumPlaintextBytes + HeaderBytes + TagBytes;

    internal static byte[] Encrypt(WindowsStorageKey key, ArtifactIdentity identity, ReadOnlySpan<byte> plaintext)
    {
        identity.Validate();
        if (plaintext.Length > MaximumPlaintextBytes)
        {
            throw new InvalidDataException("The artifact exceeds its plaintext bound.");
        }

        var envelope = new byte[HeaderBytes + plaintext.Length + TagBytes];
        "KRA1"u8.CopyTo(envelope);
        BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(4), 1);
        key.Id.TryWriteBytes(envelope.AsSpan(8, 16));
        identity.SessionId.Value.TryWriteBytes(envelope.AsSpan(24, 16));
        identity.ArtifactId.TryWriteBytes(envelope.AsSpan(40, 16));
        identity.DeletionOwnerId.Value.TryWriteBytes(envelope.AsSpan(56, 16));
        BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(72), (int)identity.Role);
        BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(76), plaintext.Length);
        RandomNumberGenerator.Fill(envelope.AsSpan(80, 12));
        using var aes = new AesGcm(key.Bytes, TagBytes);
        aes.Encrypt(envelope.AsSpan(80, 12), plaintext, envelope.AsSpan(HeaderBytes, plaintext.Length),
            envelope.AsSpan(HeaderBytes + plaintext.Length, TagBytes), envelope.AsSpan(0, HeaderBytes));
        return envelope;
    }

    internal static ArtifactIdentity ReadIdentity(byte[] envelope)
    {
        ValidateHeader(envelope);
        var role = BinaryPrimitives.ReadInt32LittleEndian(envelope.AsSpan(72));
        if (role is < (int)ArtifactRole.SessionAttachment or > (int)ArtifactRole.SessionSummary)
        {
            throw new InvalidDataException("The artifact role is unknown.");
        }
        var identity = new ArtifactIdentity(ReadSessionIdentity(envelope.AsSpan(24, 16)),
            new Guid(envelope.AsSpan(40, 16)), ReadSessionIdentity(envelope.AsSpan(56, 16)), (ArtifactRole)role);
        identity.Validate();
        return identity;
    }

    private static HostId<SessionIdentity> ReadSessionIdentity(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return new HostId<SessionIdentity>(new Guid(bytes));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The persisted artifact session identity is invalid.", exception);
        }
    }

    internal static byte[] Decrypt(WindowsStorageKey key, byte[] envelope, ArtifactReference? reference = null)
    {
        var identity = ReadIdentity(envelope);
        if (new Guid(envelope.AsSpan(8, 16)) != key.Id)
        {
            throw new InvalidDataException("The artifact does not belong to the loaded key generation.");
        }
        reference?.Validate();
        var length = BinaryPrimitives.ReadInt32LittleEndian(envelope.AsSpan(76));
        if (reference is not null
            && (reference.Identity != identity || reference.KeyId != key.Id || reference.Length != length))
        {
            throw new InvalidDataException("The artifact identity does not match its immutable reference.");
        }

        var plaintext = new byte[length];
        var transferred = false;
        try
        {
            using var aes = new AesGcm(key.Bytes, TagBytes);
            aes.Decrypt(envelope.AsSpan(80, 12), envelope.AsSpan(HeaderBytes, length),
                envelope.AsSpan(HeaderBytes + length, TagBytes), plaintext, envelope.AsSpan(0, HeaderBytes));
            if (reference is not null && !string.Equals(
                    Convert.ToHexString(SHA256.HashData(plaintext)), reference.Digest, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The artifact digest does not match its immutable reference.");
            }
            transferred = true;
            return plaintext;
        }
        finally
        {
            if (!transferred)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static void ValidateHeader(byte[] envelope)
    {
        if (envelope.Length is < HeaderBytes + TagBytes or > MaximumEnvelopeBytes
            || !envelope.AsSpan(0, 4).SequenceEqual("KRA1"u8)
            || BinaryPrimitives.ReadInt32LittleEndian(envelope.AsSpan(4)) != 1)
        {
            throw new InvalidDataException("The artifact envelope version or bound is invalid.");
        }
        var length = BinaryPrimitives.ReadInt32LittleEndian(envelope.AsSpan(76));
        if (length is < 0 or > MaximumPlaintextBytes || envelope.Length != HeaderBytes + length + TagBytes)
        {
            throw new InvalidDataException("The artifact envelope length is invalid.");
        }
    }
}
