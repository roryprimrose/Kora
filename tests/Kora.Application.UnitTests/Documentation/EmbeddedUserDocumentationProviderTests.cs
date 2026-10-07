using System.Text;
using System.Text.RegularExpressions;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.Documentation;

public sealed class EmbeddedUserDocumentationProviderTests
{
    [Fact]
    public void Embedded_sessions_guide_distinguishes_bounded_native_metadata_from_unadmitted_routing_and_history()
    {
        var pages = new EmbeddedUserDocumentationProvider().GetPages();
        var windows = pages.Single(page => string.Equals(page.Id, "windows-and-tray", StringComparison.Ordinal)).Markdown;
        windows.Should().Contain("Create empty Active session").And.Contain("Rename selected ID")
            .And.Contain("120 Unicode scalars / 480 UTF-8 bytes").And.Contain("Duplicate names")
            .And.Contain("metadata revision").And.Contain("no execution task")
            .And.Contain("v1-to-v2").And.Contain("v3 task consolidation").And.Contain("meaningful activity")
            .And.Contain("Inspect exact selected").And.Contain("Cancel inspected pre-dispatch wait");
        var commands = pages.Single(page => string.Equals(page.Id, "commands", StringComparison.Ordinal)).Markdown;
        commands.Should().Contain("Bounded exact-ID session commands").And.Contain("selected window never redirects")
            .And.Contain("session rename").And.Contain("During protected calls").And.Contain("1,024 UTF-8 bytes")
            .And.Contain("task inspect").And.Contain("task cancel").And.Contain("no effect")
            .And.Contain("previous").And.Contain("before dispatch");
        var privacy = pages.Single(page => string.Equals(page.Id, "privacy-safety-and-logs", StringComparison.Ordinal)).Markdown;
        privacy.Should().Contain("Names never").And.Contain("content digest").And.Contain("never invents titles");
    }

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
            "tools-and-built-in-skills",
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
    public void Embedded_capability_guide_distinguishes_current_behavior_from_planned_skills()
    {
        var page = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "tools-and-built-in-skills", StringComparison.Ordinal));

        page.Title.Should().Be("Tools and built-in skills: current and planned");
        page.Markdown.Should().Contain("No script-backed built-in skill executor ships in the current release.");
        page.Markdown.Should().Contain("### Lock the machine");
        page.Markdown.Should().Contain("### Shut down the computer");
        page.Markdown.Should().Contain("### Restart the computer");
        page.Markdown.Should().Contain("### Read-only integration skill");
        page.Markdown.Should().Contain("## 14. Persistent sessions, history, and shared questions");
        page.Markdown.Should().Contain("## 15. Deferred context, knowledge, and general execution");
        page.Markdown.Should().Contain("two independently executing sessions");
        page.Markdown.Should().Contain("combined script-set hash");
        page.Markdown.Should().Contain("Ignore reusable grants during calls");
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

        sections.Should().HaveCount(commands.Count + 6);
        var sessions = sections.Single(section => section.StartsWith(
            "Inspect existing minimal durable sessions", StringComparison.Ordinal));
        var sessionPhrases = Regex.Matches(sessions, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        sessionPhrases.Should().Equal("open sessions");
        var appearance = sections.Single(section => section.StartsWith(
            "Inspect or change an admitted appearance option", StringComparison.Ordinal));
        var appearancePhrases = Regex.Matches(appearance, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        appearancePhrases.Should().Equal("list appearance settings", "get appearance.theme",
            "set appearance.theme to dark", "reset appearance.theme");
        appearancePhrases.Should().OnlyContain(phrase => AppearanceCommand.Parse(phrase, "Kora") != null);
        var output = sections.Single(section => section.StartsWith(
            "Inspect or change an exact output choice", StringComparison.Ordinal));
        var outputPhrases = Regex.Matches(output, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        outputPhrases.Should().Equal("list output settings", "get speech.output-device", "status speech.output-device",
            "set speech.output-device to &lt;exact presented endpoint ID&gt;", "reset speech.output-device");
        outputPhrases.Should().OnlyContain(phrase => OutputDeviceCommand.Parse(phrase, "Kora") != null);
        var speech = sections.Single(section => section.StartsWith(
            "Inspect or change an installed speech choice", StringComparison.Ordinal));
        var speechPhrases = Regex.Matches(speech, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        speechPhrases.Should().Equal("list speech settings", "get speech.provider", "get speech.voice",
            "set speech.provider to windows-sapi", "set speech.voice to kokoro / af_heart",
            "reset speech.provider", "reset speech.voice");
        speechPhrases.Should().OnlyContain(phrase => SpeechCommand.Parse(phrase, "Kora") != null);
        var summaries = sections.Single(section => section.StartsWith(
            "Inspect or lower spoken summary caps", StringComparison.Ordinal));
        var summaryPhrases = Regex.Matches(summaries, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        summaryPhrases.Should().Equal("list speech settings", "get speech.summary-sentences", "get speech.summary-words",
            "set speech.summary-sentences to 2", "set speech.summary-words to 40",
            "reset speech.summary-sentences", "reset speech.summary-words");
        summaryPhrases.Should().OnlyContain(phrase => SpeechCommand.Parse(phrase, "Kora") != null);
        var settings = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(item => string.Equals(item.Id, "settings", StringComparison.Ordinal));
        foreach (var descriptor in AppearanceOptionRegistry.Options)
            settings.Markdown.Should().Contain(descriptor.Id);
        foreach (var descriptor in SpeechOptionRegistry.Options)
            settings.Markdown.Should().Contain(descriptor.Id);
        var sessionCommands = sections.Single(section => section.StartsWith("Bounded exact-ID session commands", StringComparison.Ordinal));
        sessionCommands.Should().Contain("session help").And.Contain("session list").And.Contain("session status")
            .And.Contain("session inspect").And.Contain("session create").And.Contain("session rename")
            .And.Contain("session done").And.Contain("session resume");
        sections = sections.Where(section => !ReferenceEquals(section, appearance)
            && !ReferenceEquals(section, output)
            && !ReferenceEquals(section, sessions) && !ReferenceEquals(section, speech)
            && !ReferenceEquals(section, sessionCommands) && !ReferenceEquals(section, summaries)).ToArray();
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
    public void Embedded_details_guide_names_actual_entry_limits_copy_and_pending_history_gates()
    {
        var pages = new EmbeddedUserDocumentationProvider().GetPages();
        var windows = pages.Single(page => string.Equals(page.Id, "windows-and-tray", StringComparison.Ordinal));
        windows.Markdown.Should().Contain("**Open details**");
        windows.Markdown.Should().Contain("**256 KiB UTF-8**");
        windows.Markdown.Should().Contain("**512 blocks**");
        windows.Markdown.Should().Contain("**4,096 nodes**");
        windows.Markdown.Should().Contain("**32 nesting levels**");
        windows.Markdown.Should().Contain("**8 open viewers**");
        windows.Markdown.Should().Contain("**Copy exact source**");
        windows.Markdown.Should().Contain("not durable conversation history");
        windows.Markdown.Should().Contain("Privacy closure clears and closes details");
        windows.Markdown.Should().Contain("assistive-technology trials remain pending");
        var responses = pages.Single(page => string.Equals(page.Id, "responses-and-calls", StringComparison.Ordinal));
        responses.Markdown.Should().Contain("not yet general compact");
        responses.Markdown.Should().Contain("Model prose cannot open/focus");
        responses.Markdown.Should().Contain("default **5 seconds**");
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
