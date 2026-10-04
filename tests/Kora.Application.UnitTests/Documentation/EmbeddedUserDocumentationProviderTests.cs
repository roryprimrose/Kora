using System.Text;
using System.Text.RegularExpressions;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.Documentation;

public sealed class EmbeddedUserDocumentationProviderTests
{
    [Fact]
    public void Embedded_guide_contains_every_documentation_page_in_navigation_order()
    {
        var provider = new EmbeddedUserDocumentationProvider();

        var first = provider.GetPages();
        var second = provider.GetPages();

        first.Should().BeSameAs(second);
        first.Select(page => page.Id).Should().Equal(
            "readme",
            "getting-started",
            "voice-and-audio",
            "responses-and-calls",
            "settings",
            "commands",
            "windows-and-tray",
            "privacy-safety-and-logs",
            "skill-and-task-execution-design",
            "troubleshooting");
        first.Should().OnlyContain(page =>
            !string.IsNullOrWhiteSpace(page.Title)
            && page.Markdown.StartsWith("# ", StringComparison.Ordinal));
        provider.GetStartPage().Should().Be(first[0]);
    }

    [Fact]
    public void Embedded_command_guide_lists_every_task_and_its_exact_variants()
    {
        var page = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "commands", StringComparison.Ordinal));
        var commands = new BuiltInCommandCatalog().GetCommands();
        var sections = Regex.Split(
                page.Markdown,
                "^### ",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(1))
            .Skip(1)
            .ToArray();

        sections.Should().HaveCount(commands.Count);
        for (var index = 0; index < commands.Count; index++)
        {
            var phrases = Regex.Matches(
                    sections[index],
                    @"^- \*\*(?<phrase>.+?)\*\*\r?$",
                    RegexOptions.Multiline,
                    TimeSpan.FromSeconds(1))
                .Select(match => match.Groups["phrase"].Value.ToLowerInvariant());

            phrases.Should().Equal(
                commands[index].AllPhrases.Select(phrase => phrase.ToLowerInvariant()),
                because: $"the {commands[index].Action} task must document all of its variants in catalog order");
        }
    }

    [Fact]
    public void Unknown_pages_follow_known_pages_in_title_order()
    {
        var provider = CreateProvider(
            ("Kora.Docs.zeta.md", "# Alpha custom"),
            ("Kora.Docs.settings.md", "# Settings"),
            ("Kora.Docs.readme.md", "# Start"),
            ("Kora.Docs.alpha.md", "# Zulu custom"),
            ("Kora.Other.ignore.md", "# Ignore"));

        var pages = provider.GetPages();

        pages.Select(page => page.Id).Should().Equal(
            "readme",
            "settings",
            "zeta",
            "alpha");
    }

    [Fact]
    public void Missing_documentation_resources_are_rejected()
    {
        var provider = CreateProvider(("Kora.Other.ignore.md", "# Ignore"));

        var action = provider.GetPages;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*No embedded Kora documentation*");
    }

    [Fact]
    public void Missing_start_page_is_rejected()
    {
        var provider = CreateProvider(("Kora.Docs.settings.md", "# Settings"));

        var action = provider.GetStartPage;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*start page*");
    }

    [Fact]
    public void Missing_resource_stream_is_rejected()
    {
        var provider = new EmbeddedUserDocumentationProvider(
            () => ["Kora.Docs.readme.md"],
            _ => null);

        var action = provider.GetPages;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*resource*unavailable*");
    }

    [Theory]
    [InlineData("No heading")]
    [InlineData("#   ")]
    public void Page_without_a_level_one_heading_is_rejected(string markdown)
    {
        var provider = CreateProvider(("Kora.Docs.readme.md", markdown));

        var action = provider.GetPages;

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*no level-one heading*");
    }

    private static EmbeddedUserDocumentationProvider CreateProvider(
        params (string Name, string Content)[] resources)
    {
        var content = resources.ToDictionary(
            item => item.Name,
            item => item.Content,
            StringComparer.Ordinal);
        return new EmbeddedUserDocumentationProvider(
            () => resources.Select(item => item.Name).ToArray(),
            name => content.TryGetValue(name, out var markdown)
                ? new MemoryStream(Encoding.UTF8.GetBytes(markdown))
                : null);
    }
}
