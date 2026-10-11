using AwesomeAssertions;

using Kora.Core.Communication;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("instructions editor", "List", null)]
    [InlineData("instructions list", "List", null)]
    [InlineData("instructions create example", "Create", "example")]
    [InlineData("Kora, instructions edit example", "Inspect", "example")]
    [InlineData("instructions inspect example", "Inspect", "example")]
    [InlineData("instructions save", "Save", null)]
    [InlineData("instructions copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("Kora, instructions copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("instructions reload", "Reload", null)]
    [InlineData("instructions discard", "Reload", null)]
    [InlineData("instructions cancel", "Cancel", null)]
    [InlineData("instructions delete", "Delete", null)]
    [InlineData("instructions activate", "Review use", null)]
    [InlineData("prompts editor", "List", null)]
    [InlineData("prompts list", "List", null)]
    [InlineData("prompts create example", "Create", "example")]
    [InlineData("Kora, prompts edit example", "Inspect", "example")]
    [InlineData("prompts inspect example", "Inspect", "example")]
    [InlineData("prompts save", "Save", null)]
    [InlineData("prompts copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("Kora, prompts copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("prompts reload", "Reload", null)]
    [InlineData("prompts discard", "Reload", null)]
    [InlineData("prompts cancel", "Cancel", null)]
    [InlineData("prompts delete", "Delete", null)]
    [InlineData("prompts activate", "Review use", null)]
    public async Task InstructionCommandsReachTheSameNativeActionsWithoutInferenceOrContentHistory(string text, string expected, string? name)
    {
        var f = await Fixture.CreateInitializedAsync();
        string? actual = null;
        string? target = null;
        f.ViewModel.OwnedInstructionEditorRequested += (action, selected) =>
        { actual = action; target = selected; return Task.CompletedTask; };
        f.ViewModel.OwnedPromptEditorRequested += (action, selected) =>
        { actual = action; target = selected; return Task.CompletedTask; };
        await f.RunAsync(text);
        actual.Should().Be(expected);
        target.Should().Be(name);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Transcript.Should().NotContain("example");
    }

    [Fact]
    public async Task MissingEditorAndInvalidTargetSyntaxRemainLocalAndShutdownDoesNotAbandonDraft()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("instructions save");
        f.ViewModel.ResponseBody.Should().Contain("unavailable");
        await f.RunAsync("instructions create");
        await f.RunAsync("instructions create example extra body");
        await f.RunAsync("instructions save example");
        await f.RunAsync("instructions unknown");
        await f.RunAsync("prompts save");
        await f.RunAsync("prompts create");
        await f.RunAsync("prompts create example extra body");
        await f.RunAsync("prompts save example");
        await f.RunAsync("prompts unknown");
        f.Reasoner.Requests.Should().BeEmpty();
        var asked = 0;
        f.ViewModel.BindInstructionEditorShutdown(() => { asked++; return Task.FromResult(false); });
        await f.ViewModel.ExitAsync();
        (await f.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        asked.Should().Be(2);
        f.ViewModel.BindInstructionEditorShutdown(() => Task.FromResult(true));
        f.ViewModel.BindOwnedInstructionFreshness(null);
        await f.ViewModel.ExitAsync();
        f.ViewModel.BindInstructionEditorShutdown(null);
    }

    [Fact]
    public async Task InstructionEditorEligibilityExpiresOnCallOrPrivacyChange()
    {
        var f = await Fixture.CreateInitializedAsync();
        var eligible = f.ViewModel.CaptureInstructionEditorEligibility();
        eligible().Should().BeTrue();
        f.CallState.SetState(CallState.Active);
        eligible().Should().BeFalse();
        f.CallState.SetState(CallState.Clear);
        eligible().Should().BeFalse();
        f.Session.IsUnlocked = false;
        f.ViewModel.CaptureInstructionEditorEligibility()().Should().BeFalse();
    }

    [Theory]
    [InlineData("missing", ArtifactKind.Instruction)]
    [InlineData("stale", ArtifactKind.Instruction)]
    [InlineData("current", ArtifactKind.Instruction)]
    [InlineData("missing", ArtifactKind.Prompt)]
    [InlineData("stale", ArtifactKind.Prompt)]
    [InlineData("current", ArtifactKind.Prompt)]
    public async Task OwnedInstructionLookupRequiresFreshnessBeforeStartingReasoning(string condition, ArtifactKind kind)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "ready");
        await f.ViewModel.InitializeAsync();
        var artifact = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("---\nkora-format: 1\nname: example\ndescription: Test\n---\nBody", false),
            kind, "kora-profile", "example").Definition;
        f.ArtifactCatalogue.Publish([artifact]);
        if (condition is not "missing")
        { f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(condition is "current")); }
        await f.RunAsync("/" + kind.ToString().ToLowerInvariant() + " example request");
        if (f.ViewModel.ActiveReasoningTask is { } task) { await task; }
        if (condition is not "current")
        {
            f.ViewModel.ResponseTitle.Should().Contain("stale or unavailable");
            f.Reasoner.Requests.Should().BeEmpty();
        }
        else
        {
            f.Reasoner.LastArtifact.Should().NotBeNull();
            f.Reasoner.LastArtifact!.IsLiteralPrompt.Should().Be(kind == ArtifactKind.Prompt);
        }
        f.ViewModel.BindOwnedInstructionFreshness(null);
    }

    [Fact]
    public async Task EditorControlsAreNotModelQuestionAnswersAndDoNotExposeTargetNamesToTheModel()
        {
            var f = new Fixture();
            f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "ready");
            f.Reasoner.Question = new("Which option?", ["One", "Two"]);
            await f.ViewModel.InitializeAsync();
            await f.RunAsync("please explain this");
            await f.ViewModel.ActiveReasoningTask!;
            var body = f.ViewModel.ResponseBody;
            var requests = f.Reasoner.Requests.Count;
            var calls = 0;
            f.ViewModel.OwnedInstructionEditorRequested += (_, _) => { calls++; return Task.CompletedTask; };
            await f.RunAsync("instructions edit private-name");
            calls.Should().Be(1);
            f.Reasoner.Requests.Should().HaveCount(requests);
            f.ViewModel.ResponseBody.Should().Be(body);
        }

    [Fact]
    public async Task UnknownAssistantPrefixDoesNotAdmitPrefixedInstructionCommands()
        {
            var f = new Fixture();
            f.NamePreferences.Name = "Supported Commands";
            await f.ViewModel.InitializeAsync();
            var calls = 0;
            f.ViewModel.OwnedInstructionEditorRequested += (_, _) => { calls++; return Task.CompletedTask; };
            await f.RunAsync("Kora, instructions editor");
            calls.Should().Be(0);
            await f.RunAsync("instructions editor");
            calls.Should().Be(1);
            f.Reasoner.Requests.Should().BeEmpty();
        }
}
