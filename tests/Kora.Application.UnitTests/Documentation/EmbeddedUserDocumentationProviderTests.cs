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
    [Theory]
    [InlineData("commands")]
    [InlineData("windows-and-tray")]
    [InlineData("tools-and-built-in-skills")]
    public void EmbeddedGuideDoesNotAdvertiseRetiredUtilities(string pageId)
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, pageId, StringComparison.Ordinal)).Markdown;

        guide.Should().NotContain("calculate <expression>")
            .And.NotContain("calculate help")
            .And.NotContain("what time is it")
            .And.NotContain("help local clock")
            .And.NotContain("host-local-date-and-time")
            .And.NotContain("Validate this snapshot as JSON")
            .And.NotContain("Validate this snapshot as XML");
    }

    [Fact]
    public void Embedded_owned_editor_guide_distinguishes_private_displayed_buffer_find_from_metadata_and_mutations()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "instruction-editor", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("## Find in the displayed owned buffer")
            .And.Contain("**Ctrl+F**").And.Contain("**Shift+F3**").And.Contain("**no wrap**")
            .And.Contain("complete count, current ordinal").And.Contain("including unsaved changes")
            .And.Contain("case-sensitive ordinal literal").And.Contain("256 UTF-16 units /")
            .And.Contain("512 UTF-8 bytes").And.Contain("never opens")
            .And.Contain("not search the").And.Contain("metadata inventory, disk")
            .And.Contain("queries and body text are not logged, transcribed or spoken")
            .And.Contain("read-only sources gain no edit or use authority");
    }

    [Fact]
    public void ConversationGuideRetiresSavedDraftManagementAndExplainsReadOnlyLegacyRecovery()
    {
        var commands = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "commands", StringComparison.Ordinal)).Markdown;
        commands.Should().Contain("Unsent input stays separately in memory for each exact session")
            .And.Contain("no ordinary draft Save/Reload/Clear")
            .And.Contain("Recover existing legacy text into empty input")
            .And.Contain("cannot overwrite nonempty unsent input")
            .And.Contain("no silent migration or purge")
            .And.Contain("Switching topics preserves text without prompting")
            .And.NotContain("open saved conversation drafts");
    }

    [Fact]
    public void Embedded_owned_editor_metadata_find_is_displayed_details_only_without_read_or_use_authority()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "instruction-editor", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("Find in selected metadata")
            .And.Contain("never searches the")
            .And.Contain("complete nonoverlapping sequence without wrapping")
            .And.Contain("source editor, unsaved draft, other rows, hidden paths")
            .And.Contain("It does not open or inspect a file")
            .And.Contain("focus failure explicitly clears")
            .And.Contain("not logged, persisted, copied, sent to a model or spoken")
            .And.Contain("Ctrl+F is not");
    }

    [Fact]
    public void Embedded_clipboard_find_guide_distinguishes_private_preparation_from_native_execution()
    {
        var pages = new EmbeddedUserDocumentationProvider().GetPages();
        var commands = pages.Single(page => string.Equals(page.Id, "commands", StringComparison.Ordinal)).Markdown;
        commands.Should().Contain("clipboard find {snapshot-id} {literal-query}")
            .And.Contain("single space after the ID is syntax")
            .And.Contain("PREPARED / REVIEW NEEDED")
            .And.Contain("before generic transcript echo")
            .And.Contain("Pending questions and approvals remain intact")
            .And.Contain("no latest-source fallback or recapture")
            .And.NotContain("**Literal Find is native-only**, not a typed/voice command");
        var windows = pages.Single(page => string.Equals(page.Id, "windows-and-tray", StringComparison.Ordinal)).Markdown;
        windows.Should().Contain("press native Find separately");
        var privacy = pages.Single(page => string.Equals(page.Id, "privacy-safety-and-logs", StringComparison.Ordinal)).Markdown;
        privacy.Should().Contain("Its unchanged literal is volatile native input only");
    }

    [Fact]
    public void Embedded_response_mode_aliases_explain_exact_future_only_controls_and_refusals()
    {
        var pages = new EmbeddedUserDocumentationProvider().GetPages();
        var commands = pages.Single(page => string.Equals(page.Id, "commands", StringComparison.Ordinal)).Markdown;
        var settings = pages.Single(page => string.Equals(page.Id, "settings", StringComparison.Ordinal)).Markdown;
        foreach (var phrase in new[] { "use visual responses", "use spoken responses", "use spoken and visual responses", "check response mode" })
        {
            commands.Should().Contain("**" + phrase + "**");
            settings.Should().Contain("**" + phrase + "**");
        }
        commands.Should().Contain("future eligible responses only").And.Contain("stale or unconfirmed state refuses")
            .And.Contain("they never answer a pending decision or reach the model");
        settings.Should().Contain("VoiceOnly cannot override call/privacy suppression or speech caps");
    }

    [Fact]
    public void EmbeddedAllWorkGuideRetiresEngineeringQueriesAndPreservesUnavailableBoundaries()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "windows-and-tray", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("Authority/queue/count ranges and retained task/request-ID query machinery are retired")
            .And.Contain("unavailable observations remain explicit rather than an empty success")
            .And.Contain("preserving complete-record byte limits, omissions and continuation")
            .And.Contain("Order affects only the loaded page")
            .And.Contain("Neither grants navigation or execution authority")
            .And.NotContain("Optional inclusive observed work metadata ranges");
    }

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
            "instruction-editor",
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
        page.Markdown.Should().Contain("General concurrent workers/provider scheduling remain unqualified");
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

        sections.Should().HaveCount(commands.Count + 20);
        var disposition = sections.Single(section => section.StartsWith("Review one exact session's logical disposition", StringComparison.Ordinal));
        disposition.Should().Contain(SessionDispositionReviewCommand.Syntax)
            .And.Contain("Nothing is removed by the command").And.Contain("separate deliberate native action")
            .And.Contain("confirm session disposition").And.Contain("never confirms")
            .And.Contain("all eight inventory counts").And.Contain("current").And.Contain("never saves");
        var conversationSearch = sections.Single(section => section.StartsWith("Search one explicitly addressed retained conversation", StringComparison.Ordinal));
        conversationSearch.Should().Contain("conversation search <session-id> -- <verbatim query>")
            .And.Contain("conversation search next <session-id>")
            .And.Contain("conversation search cancel <session-id>")
            .And.Contain("current assistant-name prefix")
            .And.Contain("not a caller-supplied cursor or JSON")
            .And.Contain("unsent buffers")
            .And.Contain("visual-only")
            .And.Contain("same")
            .And.Contain("canonical search service");
        var responseWindow = sections.Single(section => section.StartsWith(
            "Inspect or change Kora's response-window visibility preferences", StringComparison.Ordinal));
        var responseWindowPhrases = Regex.Matches(responseWindow[..responseWindow.IndexOf("These are complete", StringComparison.Ordinal)],
            @"\*\*(?<phrase>[^*]+)\*\*", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        responseWindowPhrases.Should().HaveCount(15).And.OnlyContain(phrase =>
            ResponseWindowCommand.Parse(phrase, "Kora") != null
            && ResponseWindowCommand.Parse(phrase, "Kora")!.Operation != AppearanceCommandOperation.Clarify);
        responseWindow.Should().Contain("No automatic rewrite").And.Contain("pending question/approval")
            .And.Contain("AlwaysShow=false").And.Contain("Topmost=true");
        var attachment = sections.Single(section => section.StartsWith("Inspect one retained attachment by exact session address", StringComparison.Ordinal));
        attachment.Should().Contain("session attachment inspect <session-id>").And.Contain("inspection-only")
            .And.Contain("never answers a pending question");
        var editor = sections.Single(section => section.StartsWith("Edit an owned instruction or literal prompt without a model", StringComparison.Ordinal));
        editor.Should().Contain("instructions editor").And.Contain("prompts editor")
            .And.Contain("same").And.Contain("Reload/Overwrite/Cancel").And.Contain("No paths or body dictation");
        sections.Should().Contain(section => section.StartsWith("Explicitly select a saved revision", StringComparison.Ordinal));
        var webPage = sections.Single(section => section.StartsWith(
            "Retrieve an explicit web page", StringComparison.Ordinal));
        webPage.Should().Contain("get web page https://example.com/page")
            .And.Contain("public-network")
            .And.Contain("network.get_web_page")
            .And.Contain("remains unavailable");
        var sharedSkills = sections.Single(section => section.StartsWith("Inspect shared profile skills locally", StringComparison.Ordinal));
        sharedSkills.Should().Contain("immutable").And.Contain("model exposure")
            .And.Contain("unavailable").And.Contain("removed/replaced/busy sources fail closed");
        var skillSearch = sections.Single(section => section.StartsWith("Search admitted skill package metadata", StringComparison.Ordinal));
        skillSearch.Should().Contain("ordinal-ignore-case").And.Contain("256 UTF-16 units / 512 strict UTF-8 bytes")
            .And.Contain("at most eight packages").And.Contain("at most 32 snapshots")
            .And.Contain("All / Runtime unavailable / Runtime available")
            .And.Contain("All / Instruction-compatible / Instruction-incompatible")
            .And.Contain("requires explicit result selection").And.Contain("no model/tool/typed/voice search route");
        var sessions = sections.Single(section => section.StartsWith(
            "Inspect existing minimal durable sessions", StringComparison.Ordinal));
        var sessionPhrases = Regex.Matches(sessions, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        sessionPhrases.Should().Equal("open sessions");
        var newRequest = sections.Single(section => section.StartsWith(
            "Open the existing New request composer", StringComparison.Ordinal));
        newRequest.Should().Contain("open new request").And.Contain("Activated voice")
            .And.Contain("current question or approval").And.Contain("Model-facing opening is unavailable");
        var appearance = sections.Single(section => section.StartsWith(
            "Inspect or change an admitted appearance option", StringComparison.Ordinal));
        var appearancePhrases = Regex.Matches(appearance, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        appearancePhrases.Should().Equal("list appearance settings", "get appearance.theme",
            "status appearance.reader-text-scale", "set appearance.reader-text-scale to 200",
            "set appearance.theme to dark", "reset appearance.theme");
        appearancePhrases.Should().OnlyContain(phrase => AppearanceCommand.Parse(phrase, "Kora") != null);
        var naturalAppearance = sections.Single(section => section.StartsWith(
            "Natural exact app theme and native reader text scale", StringComparison.Ordinal));
        var naturalPhrases = Regex.Matches(naturalAppearance, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        naturalPhrases.Should().Equal("use dark mode", "use light mode", "use system theme",
            "get reader text scale", "status reader text scale", "reset reader text scale",
            "set reader text scale to &lt;integer&gt; percent",
            "increase reader text scale by &lt;positive integer&gt; percentage points",
            "decrease reader text scale by &lt;positive integer&gt; percentage points");
        foreach (var phrase in naturalPhrases)
        {
            var example = phrase.Replace("&lt;integer&gt;", "125", StringComparison.Ordinal)
                .Replace("&lt;positive integer&gt;", "25", StringComparison.Ordinal);
            NaturalAppearanceCommand.Parse(example, "Kora")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
        }
        naturalAppearance.Should().Contain("current configured assistant-name prefix")
            .And.Contain("fresh confirmed state").And.Contain("percentage points")
            .And.Contain("revision-bound proposal").And.Contain("no OS/global UI scale");
        var output = sections.Single(section => section.StartsWith(
            "Inspect or change an exact output choice", StringComparison.Ordinal));
        var outputPhrases = Regex.Matches(output, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        outputPhrases.Should().Equal("list output settings", "get speech.output-device", "status speech.output-device",
            "set speech.output-device to &lt;exact presented endpoint ID&gt;", "reset speech.output-device");
        outputPhrases.Should().OnlyContain(phrase => OutputDeviceCommand.Parse(phrase, "Kora") != null);
        var volume = sections.Single(section => section.StartsWith(
            "Inspect or change Kora playback volume", StringComparison.Ordinal));
        var volumePhrases = Regex.Matches(volume, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        volumePhrases.Should().Equal("list volume settings", "get speech.playback-volume", "status speech.playback-volume",
            "set speech.playback-volume to 30", "reset speech.playback-volume", "speak louder", "speak quieter");
        volumePhrases.Should().OnlyContain(phrase => PlaybackVolumeCommand.Parse(phrase, "Kora") != null);
        var rate = sections.Single(section => section.StartsWith(
            "Inspect or change Windows-native speech rate", StringComparison.Ordinal));
        var ratePhrases = Regex.Matches(rate, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        ratePhrases.Should().Equal("list rate settings", "get speech.windows-rate", "status speech.windows-rate",
            "set speech.windows-rate to -10", "set speech.windows-rate to 0", "set speech.windows-rate to 10", "reset speech.windows-rate",
            "speak faster", "speak slower");
        ratePhrases.Should().OnlyContain(phrase => WindowsSpeechRateCommand.Parse(phrase, "Kora") != null);
        rate.Should().Contain("Kokoro is unsupported and unchanged").And.Contain("default/reset **0**");
        var responseMode = sections.Single(section => section.StartsWith(
            "Inspect or change the device-default response mode", StringComparison.Ordinal));
        var responseModePhrases = Regex.Matches(responseMode, @"^- \*\*(?<phrase>.+?)\*\*\r?$",
            RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["phrase"].Value).ToArray();
        responseModePhrases.Should().Equal("list response settings", "get responses.default-mode", "status responses.default-mode",
            "set responses.default-mode to Hybrid", "set responses.default-mode to VoiceOnly",
            "set responses.default-mode to VisualOnly", "reset responses.default-mode");
        responseModePhrases.Should().OnlyContain(phrase => ResponseModeCommand.Parse(phrase, "Kora") != null);
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
        summaryPhrases.Should().Equal("show spoken summary limits", "set spoken summary sentence limit to 2",
            "set spoken summary word limit to 40", "reset spoken summary sentence limit", "reset spoken summary word limit",
            "list speech settings", "get speech.summary-sentences", "get speech.summary-words",
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
            && !ReferenceEquals(section, naturalAppearance)
            && !ReferenceEquals(section, disposition)
            && !ReferenceEquals(section, responseWindow)
            && !ReferenceEquals(section, output)
            && !ReferenceEquals(section, volume)
            && !ReferenceEquals(section, rate)
            && !ReferenceEquals(section, responseMode)
            && !ReferenceEquals(section, sessions) && !ReferenceEquals(section, speech)
            && !ReferenceEquals(section, sessionCommands) && !ReferenceEquals(section, summaries)).ToArray();
        sections = sections.Where(section => !ReferenceEquals(section, sharedSkills)
            && !ReferenceEquals(section, webPage) && !ReferenceEquals(section, conversationSearch)
            && !ReferenceEquals(section, attachment) && !ReferenceEquals(section, newRequest)).ToArray();
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
