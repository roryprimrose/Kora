namespace Kora.Core.Artifacts;

public sealed class OwnedDraftLineIndex
{
    private readonly int[] starts;

    public OwnedDraftLineIndex(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > ArtifactDefinition.MaximumContentLength)
        { throw new InvalidDataException("Go to line requires a displayed buffer within the owned source character bound."); }
        var lines = new List<int> { 0 };
        for (var offset = 0; offset < source.Length; offset++)
        {
            if (source[offset] == '\r')
            {
                if (offset + 1 < source.Length && source[offset + 1] == '\n') { offset++; }
                lines.Add(offset + 1);
            }
            else if (source[offset] == '\n') { lines.Add(offset + 1); }
        }
        starts = lines.ToArray();
    }

    public int Count => starts.Length;

    public bool TryResolve(string input, out int offset)
    {
        ArgumentNullException.ThrowIfNull(input);
        offset = 0;
        if (input.Length == 0 || input[0] is < '1' or > '9') { return false; }
        var line = 0;
        foreach (var digit in input)
        {
            if (digit is < '0' or > '9') { return false; }
            var value = digit - '0';
            if (line > (Count - value) / 10 || line * 10 + value > Count) { return false; }
            line = line * 10 + value;
        }
        offset = starts[line - 1];
        return true;
    }
}
