using System.Globalization;
using Kora.Core.Context;

namespace Kora.Core.KnowledgeSources;

public sealed record KnowledgeSourceCaptureLimits
{
    public static KnowledgeSourceCaptureLimits HardMaximum { get; } =
        new(KnowledgeSourcePolicy.MaximumFiles, LocalFilePolicy.MaximumBytes, KnowledgeSourcePolicy.MaximumCombinedBytes);

    public KnowledgeSourceCaptureLimits(int maximumFiles, long maximumFileBytes, long maximumCombinedBytes)
    {
        if (maximumFiles is <= 0 or > KnowledgeSourcePolicy.MaximumFiles
            || maximumFileBytes is <= 0 or > LocalFilePolicy.MaximumBytes
            || maximumCombinedBytes is <= 0 or > KnowledgeSourcePolicy.MaximumCombinedBytes)
        { throw new InvalidDataException("Capture limits must be positive and no greater than the existing hard maxima."); }
        MaximumFiles = maximumFiles;
        MaximumFileBytes = maximumFileBytes;
        MaximumCombinedBytes = maximumCombinedBytes;
    }

    public int MaximumFiles { get; }
    public long MaximumFileBytes { get; }
    public long MaximumCombinedBytes { get; }

    public string Serialize() => string.Create(CultureInfo.InvariantCulture,
        $"1:{MaximumFiles}:{MaximumFileBytes}:{MaximumCombinedBytes}");

    public static KnowledgeSourceCaptureLimits Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split(':');
        if (parts.Length != 4 || !string.Equals(parts[0], "1", StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var files)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var perFile)
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var combined))
        { throw new InvalidDataException("Unknown or malformed capture limits preference."); }
        var limits = new KnowledgeSourceCaptureLimits(files, perFile, combined);
        if (!string.Equals(limits.Serialize(), text, StringComparison.Ordinal))
        { throw new InvalidDataException("Capture limits preference is not canonical."); }
        return limits;
    }
}
