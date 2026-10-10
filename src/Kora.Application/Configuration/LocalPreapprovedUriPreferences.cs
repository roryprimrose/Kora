using Kora.Core.Network;

namespace Kora.Application.Configuration;

public sealed class LocalPreapprovedUriPreferences : IPreapprovedUriPreferences
{
    private const string FileName = "preapproved-uris.txt";
    private readonly IPreferenceStore store;

    public LocalPreapprovedUriPreferences(Kora.Core.Dependencies.IApplicationDataPaths paths)
        : this(new LocalPreferenceStore(paths))
    {
    }

    internal LocalPreapprovedUriPreferences(IPreferenceStore store)
    {
        this.store = store;
    }

    public PreapprovedUriSettings Load()
    {
        var text = store.ReadText(
            FileName,
            PreapprovedUriPattern.MaximumTotalUtf8Bytes
                + PreapprovedUriPattern.MaximumPatternCount * Environment.NewLine.Length
                + 2);
        if (text is null)
        {
            return PreapprovedUriSettings.Empty;
        }

        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved preapproved URI format is unknown or malformed.");
        }

        var patterns = new List<string>();
        while (reader.ReadLine() is { } pattern)
        {
            patterns.Add(pattern);
        }

        try
        {
            return PreapprovedUriSettings.Create(patterns);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The saved preapproved URI patterns are invalid.", exception);
        }
    }

    public void Save(PreapprovedUriSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validated = PreapprovedUriSettings.Create(settings.Patterns);
        store.WriteLines(FileName, Serialize(validated));
    }

    private static IEnumerable<string> Serialize(PreapprovedUriSettings settings)
    {
        yield return "1";
        foreach (var pattern in settings.Patterns)
        {
            yield return pattern;
        }
    }
}
