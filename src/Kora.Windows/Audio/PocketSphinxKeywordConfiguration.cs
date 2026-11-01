using System.Text;
using Kora.Core.Configuration;

namespace Kora.Windows.Audio;

internal sealed record PocketSphinxKeywordConfiguration(HandsFreeNativeProfile Profile, string AssistantName, string Keyphrase)
{
    internal static async Task<PocketSphinxKeywordConfiguration> CreateAsync(
        HandsFreeNativeProfile profile, string name, CancellationToken cancellationToken)
    {
        if (!profile.CaptureQualified || !profile.Names.Contains(name)
            || !string.Equals(AssistantNameRules.Normalize(name), name, StringComparison.Ordinal) || name.Any(character => !char.IsAscii(character)))
        {
            throw new InvalidOperationException("The configured name is not independently qualified for this exact profile.");
        }
        var words = name.Split(' ');
        var mapped = new string?[words.Length];
        await using var stream = new FileStream(profile.DictionaryPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > 4 * 1024 * 1024)
        {
            throw new InvalidDataException("The exact wake dictionary exceeds the supported byte bound.");
        }
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        var count = 0;
        async Task<string?> ReadLineAsync()
        {
            try { return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false); }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("The exact wake dictionary is not strict UTF-8.", exception);
            }
        }
        while (await ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (++count > 200000 || line.Length > 1024 || line.Any(character => !char.IsAscii(character) || character == '\0'))
            {
                throw new InvalidDataException("The exact wake dictionary violates its supported grammar or resource bounds.");
            }
            if (line.StartsWith("##", StringComparison.Ordinal) || line.StartsWith(";;", StringComparison.Ordinal)) { continue; }
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) { continue; }
            if (tokens.Length is < 2 or > 65 || tokens.Any(token => token.Length > 64))
            {
                throw new InvalidDataException("The exact wake dictionary contains an invalid pronunciation entry.");
            }
            for (var index = 0; index < words.Length; index++)
            {
                if (!string.Equals(tokens[0], words[index], StringComparison.OrdinalIgnoreCase)) { continue; }
                if (tokens[0].Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('\'' or '-'))
                    || tokens.Skip(1).Any(token => token.Any(character => !char.IsAsciiLetterOrDigit(character))))
                {
                    throw new InvalidDataException("The qualified name has no supported literal dictionary mapping.");
                }
                if (mapped[index] is not null) { throw new InvalidDataException("The qualified name has an ambiguous dictionary entry."); }
                mapped[index] = tokens[0];
            }
        }
        if (mapped.Any(word => word is null))
        {
            throw new InvalidOperationException("The qualified configured name is absent from the exact installed dictionary. No pronunciation or name fallback is generated.");
        }
        return new(profile, name, string.Join(' ', mapped));
    }
}
