using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class BuiltInCommandRouterTests
{
    private readonly BuiltInCommandCatalog catalog = new();

    [Theory]
    [InlineData("Kora, lock the machine.", BuiltInAction.LockMachine)]
    [InlineData("lock my computer", BuiltInAction.LockMachine)]
    [InlineData("KORA — WHAT CAN YOU DO?", BuiltInAction.ShowHelp)]
    [InlineData("Kora, show the user guide", BuiltInAction.OpenDocumentation)]
    [InlineData("restart your application", BuiltInAction.RestartApplication)]
    [InlineData("Kora, reboot this machine", BuiltInAction.ProposeRestart)]
    [InlineData("Kora, what is the current task status", BuiltInAction.ShowCurrentTaskProgress)]
    [InlineData("how far along is the current task", BuiltInAction.ShowCurrentTaskProgress)]
    [InlineData("stop listening", BuiltInAction.DisableListening)]
    [InlineData("Kora, disable listening.", BuiltInAction.DisableListening)]
    [InlineData("stop all work", BuiltInAction.StopAllWork)]
    [InlineData("Kora, STOP ALL WORK!", BuiltInAction.StopAllWork)]
    [InlineData("stop", BuiltInAction.CancelTask)]
    [InlineData("stop speaking", BuiltInAction.StopSpeaking)]
    public void Match_recognizes_exact_built_in_phrases(string transcript, BuiltInAction expectedAction)
    {
        var router = new BuiltInCommandRouter(catalog);

        var result = router.Match(transcript);

        result.IsMatch.Should().BeTrue();
        result.Command.Should().NotBeNull();
        result.Command!.Action.Should().Be(expectedAction);
    }

    [Theory]
    [InlineData("Kora, bring up Kora", BuiltInAction.ShowApplication)]
    [InlineData("close your window", BuiltInAction.HideApplication)]
    [InlineData("close Kora", BuiltInAction.ExitApplication)]
    [InlineData("restart the app", BuiltInAction.RestartApplication)]
    [InlineData("open preferences", BuiltInAction.OpenSettings)]
    [InlineData("open the manual", BuiltInAction.OpenDocumentation)]
    [InlineData("review local model setup", BuiltInAction.OpenSetup)]
    [InlineData("what can I say?", BuiltInAction.ShowHelp)]
    [InlineData("which version of Kora is this?", BuiltInAction.ShowVersion)]
    [InlineData("show the task queue", BuiltInAction.ShowStatus)]
    [InlineData("what's the progress of the current task?", BuiltInAction.ShowCurrentTaskProgress)]
    [InlineData("stop generating", BuiltInAction.CancelTask)]
    [InlineData("be quiet", BuiltInAction.StopSpeaking)]
    [InlineData("lock my screen", BuiltInAction.LockMachine)]
    [InlineData("turn off my computer", BuiltInAction.ProposeShutdown)]
    [InlineData("reboot my computer", BuiltInAction.ProposeRestart)]
    [InlineData("don't shut down the computer", BuiltInAction.CancelPowerAction)]
    [InlineData("is a shutdown pending?", BuiltInAction.ShowPowerStatus)]
    [InlineData("show model settings", BuiltInAction.ShowModelExecution)]
    [InlineData("turn on local models", BuiltInAction.EnableLocalModels)]
    [InlineData("stop using local models", BuiltInAction.DisableLocalModels)]
    [InlineData("enable cloud models", BuiltInAction.EnableHostedModels)]
    [InlineData("turn off hosted models", BuiltInAction.DisableHostedModels)]
    public void Match_routes_conversational_variants_to_the_intended_action(
        string transcript,
        BuiltInAction expectedAction)
    {
        new BuiltInCommandRouter(catalog).Match(transcript).Command?.Action
            .Should().Be(expectedAction);
    }

    [Theory]
    [InlineData("restart")]
    [InlineData("please lock the machine")]
    [InlineData("Kora")]
    [InlineData("explain how to lock the machine")]
    [InlineData("do what the document says")]
    [InlineData("could you close Kora after answering this?")]
    [InlineData("how do I lock my screen?")]
    [InlineData("please turn off my computer")]
    [InlineData("tell me how to restart my PC")]
    [InlineData("please stop listening")]
    [InlineData("explain stop listening")]
    [InlineData("stop listening and exit")]
    [InlineData("enable listening")]
    [InlineData("voice unmute")]
    [InlineData("please stop all work")]
    [InlineData("stop all")]
    [InlineData("stop all work and exit")]
    [InlineData("explain stop all work")]
    [InlineData("stop all works")]
    public void Match_rejects_ambiguous_or_non_command_text(string transcript)
    {
        var router = new BuiltInCommandRouter(catalog);

        var result = router.Match(transcript);

        result.IsMatch.Should().BeFalse();
        result.Command.Should().BeNull();
    }

    [Fact]
    public void Catalog_has_unique_normalized_phrases()
    {
        var phrases = catalog.GetCommands().SelectMany(command => command.AllPhrases).ToArray();

        phrases.Should().HaveCount(phrases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("Stop All")]
    [InlineData("Stop All Work")]
    [InlineData("Nova")]
    public void Stop_all_work_is_an_exact_original_command_with_no_new_aliases(string name)
    {
        var commands = catalog.GetCommands(name);
        var command = commands.Single(item => item.Action == BuiltInAction.StopAllWork);
        command.Aliases.Should().BeEmpty();
        command.SupportsModelSuggestion.Should().BeFalse();
        ((int)BuiltInAction.DisableListening).Should().Be(25);
        ((int)command.Action).Should().Be(26);
        new BuiltInCommandRouter(catalog).Match($"{name}, stop all work", name).Command.Should().Be(command);
    }

    [Fact]
    public void Every_catalog_phrase_routes_to_its_declared_action()
    {
        var router = new BuiltInCommandRouter(catalog);

        foreach (var command in catalog.GetCommands())
        {
            foreach (var phrase in command.AllPhrases)
            {
                router.Match(phrase).Command?.Action.Should().Be(
                    command.Action,
                    because: $"'{phrase}' is registered for {command.Action}");
                router.Match($"Kora, {phrase}.").Command?.Action.Should().Be(
                    command.Action,
                    because: $"wake-prefixed '{phrase}' must use the same route");
            }
        }
    }

    [Fact]
    public void Match_preserves_original_and_normalized_transcripts()
    {
        var router = new BuiltInCommandRouter(catalog);

        var result = router.Match(" Kora, SHOW   your window! ");

        result.Transcript.Should().Be(" Kora, SHOW   your window! ");
        result.NormalizedTranscript.Should().Be("show your window");
    }

    [Fact]
    public void Match_uses_the_configured_name_without_retaining_the_default_alias()
    {
        var router = new BuiltInCommandRouter(catalog);

        var customMatch = router.Match("Nova, what can you do?", "Nova");
        var oldNameMatch = router.Match("Kora, what can you do?", "Nova");

        customMatch.Command?.Action.Should().Be(BuiltInAction.ShowHelp);
        customMatch.NormalizedTranscript.Should().Be("what can you do");
        oldNameMatch.IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("Nova, stop listening", true)]
    [InlineData("NOVA, DISABLE LISTENING!", true)]
    [InlineData("Kora, stop listening", false)]
    [InlineData("Nova, please stop listening", false)]
    public void Listening_stop_uses_only_the_exact_current_prefix(string text, bool matched)
    {
        var result = new BuiltInCommandRouter(catalog).Match(text, "Nova");

        result.IsMatch.Should().Be(matched);
        if (matched) { result.Command!.Action.Should().Be(BuiltInAction.DisableListening); }
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("Disable")]
    [InlineData("Stop Listening")]
    [InlineData("Disable Listening")]
    public void Listening_stop_remains_reserved_when_the_current_name_overlaps_a_stop_phrase(string name)
    {
        var router = new BuiltInCommandRouter(catalog);
        foreach (var phrase in new[] { "stop listening", "disable listening" })
        {
            router.Match(phrase, name).Command!.Action.Should().Be(BuiltInAction.DisableListening);
            router.Match($"{name}, {phrase}", name).Command!.Action.Should().Be(BuiltInAction.DisableListening);
        }
    }

    [Fact]
    public void Catalog_renders_name_specific_phrases_and_descriptions()
    {
        var commands = catalog.GetCommands("Nova");

        commands.Should().Contain(command =>
            command.CanonicalPhrase == "show nova"
            && command.Description == "Show the Nova window.");
        commands.SelectMany(command => command.AllPhrases)
            .Should().NotContain(phrase => phrase.Contains("kora", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Match_uses_the_configured_name_in_new_application_variants()
    {
        var router = new BuiltInCommandRouter(catalog);

        router.Match("Nova, bring up Nova", "Nova").Command?.Action
            .Should().Be(BuiltInAction.ShowApplication);
        router.Match("Nova, close Nova", "Nova").Command?.Action
            .Should().Be(BuiltInAction.ExitApplication);
        router.Match("Nova, which version of Nova is this?", "Nova").Command?.Action
            .Should().Be(BuiltInAction.ShowVersion);
        router.Match("Nova, close Kora", "Nova").IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("Supported Commands", "show supported commands")]
    [InlineData("Your-Version", "show your version")]
    public void Catalog_rejects_a_name_that_conflicts_with_a_normalized_builtin_phrase(
        string name,
        string conflictingPhrase)
    {
        var action = () => catalog.GetCommands(name);

        action.Should().Throw<ArgumentException>()
            .WithMessage($"*conflicts*{conflictingPhrase}*");
    }

    [Fact]
    public void Match_rejects_a_bare_custom_activation_name()
    {
        var result = new BuiltInCommandRouter(catalog).Match("Nova", "Nova");

        result.IsMatch.Should().BeFalse();
        result.NormalizedTranscript.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Match_rejects_missing_transcripts(string? transcript)
    {
        var router = new BuiltInCommandRouter(catalog);

        var action = () => router.Match(transcript!);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Kora, stop speaking", "Kora", true)]
    [InlineData("NOVA — open settings", "Nova", true)]
    [InlineData("stop speaking", "Kora", false)]
    [InlineData("Koral, stop speaking", "Kora", false)]
    [InlineData("Kora", "Kora", false)]
    public void IsActivationPrefixed_detects_a_complete_configured_name_prefix(
        string transcript,
        string assistantName,
        bool expected)
    {
        var router = new BuiltInCommandRouter(catalog);

        router.IsActivationPrefixed(transcript, assistantName).Should().Be(expected);
    }

    [Theory]
    [InlineData("Say Kora, open documentation for help.", "Kora open documentation", true)]
    [InlineData("No matching command is being spoken.", "Kora stop", false)]
    public void ContainsNormalizedPhrase_ignores_punctuation_and_casing(
        string text,
        string phrase,
        bool expected)
    {
        var router = new BuiltInCommandRouter(catalog);

        router.ContainsNormalizedPhrase(text, phrase).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "Kora")]
    [InlineData("", "Kora")]
    [InlineData("  ", "Kora")]
    public void IsActivationPrefixed_rejects_missing_transcripts(
        string? transcript,
        string assistantName)
    {
        var router = new BuiltInCommandRouter(catalog);

        var action = () => router.IsActivationPrefixed(transcript!, assistantName);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, "Kora stop")]
    [InlineData("Kora is speaking", null)]
    [InlineData("", "Kora stop")]
    [InlineData("Kora is speaking", " ")]
    public void ContainsNormalizedPhrase_rejects_missing_values(
        string? text,
        string? phrase)
    {
        var router = new BuiltInCommandRouter(catalog);

        var action = () => router.ContainsNormalizedPhrase(text!, phrase!);

        action.Should().Throw<ArgumentException>();
    }
}