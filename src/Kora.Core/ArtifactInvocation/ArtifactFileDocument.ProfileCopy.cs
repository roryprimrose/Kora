namespace Kora.Core.Artifacts;

public sealed partial class ArtifactFileDocument
{
    public void RequireProfileInstructionSource()
    {
        if (Definition.Kind != ArtifactKind.Instruction
            || !string.Equals(Definition.Source, "profile-instruction", StringComparison.Ordinal)
            || !string.Equals(Metadata.GetValueOrDefault("kora-format"), "1", StringComparison.Ordinal))
        { throw new InvalidDataException("A profile fork requires an exact native kora-format: 1 instruction, not a skill, prompt, agent or legacy file."); }
        RequireSupportedEditorFormat();
    }

    public ArtifactFileDocument WithProfileInstructionCopyName(string name, string sourceIdentity)
    {
        RequireProfileInstructionSource();
        OwnedInstructionNames.WriteName(name);
        _ = ArtifactDigest.Validate(sourceIdentity);
        if (string.Equals(name, Definition.CommandName, StringComparison.Ordinal))
        { throw new InvalidDataException("Choose a fresh name distinct from the original instruction name."); }
        var owned = Parse(bytes, ArtifactKind.Instruction, "kora-profile", name).WithOwnedCopyName(name);
        var attribution = "Profile instruction fork " + sourceIdentity;
        if (Metadata.TryGetValue("attribution", out var original))
        { attribution += "; original attribution:" + (original.Length > 0 ? " " + original : string.Empty); }
        var text = owned.Text;
        var offset = NextLine(text, 0);
        // Parse already established a complete scalar header and its closing delimiter.
        while (true)
        {
            var next = NextLine(text, offset);
            var line = text[offset..next].TrimEnd('\r', '\n');
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                var newline = text.AsSpan(3).StartsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
                    : text[3] == '\r' ? "\r" : "\n";
                text = text[..offset] + "attribution: " + attribution + newline + text[offset..];
                break;
            }
            var separator = line.IndexOf(':');
            if (string.Equals(line[..separator].Trim(), "attribution", StringComparison.Ordinal))
            {
                // Retain the original scalar's quoting, whitespace and exact newline.
                var start = separator + 1;
                var finish = line.Length;
                while (start < finish && char.IsWhiteSpace(line[start])) { start++; }
                while (finish > start && char.IsWhiteSpace(line[finish - 1])) { finish--; }
                if (finish - start >= 2 && line[start] == line[finish - 1] && line[start] is '\'' or '"')
                { start++; finish--; }
                text = text[..(offset + start)] + attribution + text[(offset + finish)..];
                break;
            }
            offset = next;
        }
        var copy = Parse(Encode(text, HasBom), ArtifactKind.Instruction, "kora-profile", name);
        copy.RequireOwnedCopySource();
        return copy;
    }
}