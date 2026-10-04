namespace Kora.Application.Documentation;

public sealed class EmbeddedUserDocumentationProvider : IUserDocumentationProvider
{
    private const string ResourcePrefix = "Kora.Docs.";
    private const string ResourceSuffix = ".md";
    private const string StartPageId = "readme";

    private static readonly string[] PreferredOrder =
    [
        StartPageId,
        "getting-started",
        "voice-and-audio",
        "responses-and-calls",
        "settings",
        "commands",
        "windows-and-tray",
        "privacy-safety-and-logs",
        "skill-and-task-execution-design",
        "troubleshooting",
    ];

    private readonly Func<string[]> getResourceNames;
    private readonly Func<string, Stream?> openResource;
    private IReadOnlyList<UserDocumentationPage>? pages;

    public EmbeddedUserDocumentationProvider()
        : this(
            typeof(EmbeddedUserDocumentationProvider).Assembly.GetManifestResourceNames,
            typeof(EmbeddedUserDocumentationProvider).Assembly.GetManifestResourceStream)
    {
    }

    internal EmbeddedUserDocumentationProvider(
        Func<string[]> getResourceNames,
        Func<string, Stream?> openResource)
    {
        this.getResourceNames = getResourceNames;
        this.openResource = openResource;
    }

    public IReadOnlyList<UserDocumentationPage> GetPages() =>
        pages ??= LoadPages();

    public UserDocumentationPage GetStartPage() =>
        GetPages().FirstOrDefault(page => string.Equals(
            page.Id,
            StartPageId,
            StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            "The embedded documentation start page is unavailable.");

    private UserDocumentationPage[] LoadPages()
    {
        var order = PreferredOrder
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var result = getResourceNames()
            .Where(name =>
                name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(LoadPage)
            .OrderBy(page => order.GetValueOrDefault(page.Id, int.MaxValue))
            .ThenBy(page => page.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (result.Length == 0)
        {
            throw new InvalidOperationException(
                "No embedded Kora documentation pages are available.");
        }

        return result;
    }

    private UserDocumentationPage LoadPage(string resourceName)
    {
        using var stream = openResource(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded documentation resource '{resourceName}' is unavailable.");
        using var reader = new StreamReader(stream);
        var markdown = reader.ReadToEnd();
        var id = resourceName[ResourcePrefix.Length..^ResourceSuffix.Length];
        var title = markdown
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..]
            .Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidDataException(
                $"The embedded documentation page '{id}' has no level-one heading.");
        }

        return new UserDocumentationPage(id, title, markdown);
    }
}
