using System.Buffers.Binary;
using System.Text.Json;

namespace Kora.Windows.Coordination;

internal static class InstanceProtocol
{
    internal const int Version = 1;
    internal const int MaximumBytes = 4096;

    internal static async Task WriteAsync(Stream stream, InstanceMessage message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > MaximumBytes)
        {
            throw new InvalidDataException("The lifecycle message exceeds protocol limits.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<InstanceMessage> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumBytes)
        {
            throw new InvalidDataException("The lifecycle message has an invalid frame length.");
        }

        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<InstanceMessage>(bytes)
            ?? throw new InvalidDataException("The lifecycle message is missing.");
        if (result.Kind is null || result.Ticket is null || result.Detail is null ||
            result.Version != Version || result.Kind.Length > 32 || result.Ticket.Length > 64 ||
            result.Detail.Length > 1024)
        {
            throw new InvalidDataException("The lifecycle protocol or message is unsupported.");
        }

        return result;
    }
}
