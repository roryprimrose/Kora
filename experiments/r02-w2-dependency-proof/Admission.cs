using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace W2Proof;

public sealed class InputFile(string name, byte[] bytes)
{
    private readonly byte[] content = bytes.ToArray();
    public string Name { get; } = name;
    public byte[] Bytes => content.ToArray();
}
public sealed record EffectReceipt(string RunId, int Pid, string AdmissionDigest, int Value);

public static class Admission
{
    public static string Digest(string domain, IReadOnlyList<InputFile> files)
    {
        if (files.Count is < 1 or > 128) throw new InvalidDataException("Invalid input count");
        if (files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("Duplicate input identity");
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(domain + "\0"));
        Span<byte> integer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(integer, checked((uint)files.Count));
        stream.Write(integer[..4]);
        foreach (var file in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (!ValidName(file.Name) || file.Bytes.Length > 1024 * 1024)
                throw new InvalidDataException("Invalid bounded input");
            var name = Encoding.UTF8.GetBytes(file.Name);
            BinaryPrimitives.WriteUInt32BigEndian(integer, checked((uint)name.Length));
            stream.Write(integer[..4]);
            stream.Write(name);
            BinaryPrimitives.WriteUInt64BigEndian(integer, checked((ulong)file.Bytes.Length));
            stream.Write(integer);
            stream.Write(file.Bytes);
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public static bool ValidName(string name) => !string.IsNullOrEmpty(name)
        && name.Split('\\').All(s => s.Length > 0 && s is not "." and not "..")
        && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '\\');

    public static string Outcome(string run, int pid, string digest, EffectReceipt? receipt) =>
        receipt is { Value: 102 } && receipt.RunId == run && receipt.Pid == pid
        && receipt.AdmissionDigest == digest ? "Observed" : "Unknown";

    public static string ClassifyNative(int error) => error is 5 or 367 or 1260 ? "Denied" : "Unknown";
}
