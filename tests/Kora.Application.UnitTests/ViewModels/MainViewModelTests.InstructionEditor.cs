using AwesomeAssertions;

using Kora.Core.Communication;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("before-model")]
    [InlineData("before-answer")]
    [InlineData("removed-before-model")]
    [InlineData("current")]
    public async Task FileBackedInvocationRevalidatesWithoutWatcherDeliveryAndNeverPublishesStaleAnswers(string change)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "ready");
        await f.ViewModel.InitializeAsync();
        var definition = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("---\nname: example\ndescription: Test\n---\nBody", false),
            ArtifactKind.Skill, "user-profile", "example").Definition with { UseControlId = "file.example" };
        f.ArtifactCatalogue.Publish([definition]);
        var checks = 0;
        if (change is not "missing")
        {
            f.ViewModel.BindFileArtifactFreshness((candidate, token) =>
            {
                candidate.Should().BeSameAs(definition);
                token.IsCancellationRequested.Should().BeFalse();
                checks++;
                if (change is "removed-before-model")
                {
                    f.ArtifactCatalogue.Publish([UpdatedLockArtifact()]);
                    return Task.FromResult(true);
                }
                return Task.FromResult(change is "current" || change is "before-model" && checks < 2
                    || change is "before-answer" && checks < 3);
            });
        }
        await f.RunAsync("/example original request");
        if (f.ViewModel.ActiveReasoningTask is { } task) { await task; }
        f.Reasoner.Requests.Should().HaveCount(change is "before-answer" or "current" ? 1 : 0);
        f.ViewModel.IsModelQuestionPending.Should().BeFalse();
        f.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        if (change is "current") { f.ViewModel.ResponseBody.Should().Be("Generated locally; verify important details.\n\nA local answer."); }
        else { f.ViewModel.ResponseBody.Should().NotContain("A local answer."); }
        f.ViewModel.BindFileArtifactFreshness(null);
    }

    [Fact]
    public async Task CancellationDuringFileFreshnessReadJoinsTheReadAndCannotStartTheModel()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "ready");
        await f.ViewModel.InitializeAsync();
        var definition = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nname: example\ndescription: Test\n---\nBody", false),
            ArtifactKind.Skill, "user-profile", "example").Definition with { UseControlId = "file.example" };
        f.ArtifactCatalogue.Publish([definition]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        f.ViewModel.BindFileArtifactFreshness(async (_, token) =>
        {
            if (++reads == 1) { return true; }
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.SetResult();
            return await release.Task;
        });
        await f.RunAsync("/example original request");
        var reasoning = f.ViewModel.ActiveReasoningTask!;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var stopping = f.ViewModel.CancelCurrentTaskAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        reasoning.IsCompleted.Should().BeFalse("the source read still owns its lifetime");
        release.SetResult(true);
        await stopping;
        await reasoning;
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.ResponseBody.Should().NotContain("A local answer.");
    }

    [Theory]
    [InlineData("instructions editor", "List", null)]
    [InlineData("instructions list", "List", null)]
    [InlineData("instructions create example", "Create", "example")]
    [InlineData("Kora, instructions edit example", "Inspect", "example")]
    [InlineData("instructions inspect example", "Inspect", "example")]
    [InlineData("instructions save", "Save", null)]
    [InlineData("instructions validate", "Validate", null)]
    [InlineData("Kora, instructions validate", "Validate", null)]
    [InlineData("instructions compare", "Compare", null)]
    [InlineData("Kora, instructions compare", "Compare", null)]
    [InlineData("instructions rebase", "Rebase", null)]
    [InlineData("Kora, instructions rebase", "Rebase", null)]
    [InlineData("instructions refine", "Refine", null)]
    [InlineData("Kora, instructions refine", "Refine", null)]
    [InlineData("instructions copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("Kora, instructions copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("instructions reload", "Reload", null)]
    [InlineData("instructions discard", "Reload", null)]
    [InlineData("instructions cancel", "Cancel", null)]
    [InlineData("instructions delete", "Delete", null)]
    [InlineData("instructions activate", "Review use", null)]
    [InlineData("instructions disable", "Disable reviewed use", null)]
    [InlineData("Kora, instructions stop-use", "Disable reviewed use", null)]
    [InlineData("prompts editor", "List", null)]
    [InlineData("prompts list", "List", null)]
    [InlineData("prompts create example", "Create", "example")]
    [InlineData("Kora, prompts edit example", "Inspect", "example")]
    [InlineData("prompts inspect example", "Inspect", "example")]
    [InlineData("prompts save", "Save", null)]
    [InlineData("prompts validate", "Validate", null)]
    [InlineData("Kora, prompts validate", "Validate", null)]
    [InlineData("prompts compare", "Compare", null)]
    [InlineData("Kora, prompts compare", "Compare", null)]
    [InlineData("prompts rebase", "Rebase", null)]
    [InlineData("Kora, prompts rebase", "Rebase", null)]
    [InlineData("prompts copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("Kora, prompts copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("prompts reload", "Reload", null)]
    [InlineData("prompts discard", "Reload", null)]
    [InlineData("prompts cancel", "Cancel", null)]
    [InlineData("prompts delete", "Delete", null)]
    [InlineData("prompts activate", "Review use", null)]
    [InlineData("prompts disable", "Disable reviewed use", null)]
    [InlineData("Kora, prompts stop-use", "Disable reviewed use", null)]
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
        var observationCurrent = f.ViewModel.CaptureDefinitionObservationEligibility();
        observationCurrent().Should().BeTrue();
        await f.RunAsync(text);
        observationCurrent().Should().BeFalse();
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
        await f.RunAsync("instructions validate example");
        await f.RunAsync("instructions compare example");
        await f.RunAsync("instructions rebase example");
        await f.RunAsync("instructions refine example");
        await f.RunAsync("prompts refine");
        await f.RunAsync("instructions unknown");
        await f.RunAsync("instructions disable example");
        await f.RunAsync("instructions stop-use extra");
        await f.RunAsync("prompts save");
        await f.RunAsync("prompts create");
        await f.RunAsync("prompts create example extra body");
        await f.RunAsync("prompts save example");
        await f.RunAsync("prompts validate body");
        await f.RunAsync("prompts compare example");
        await f.RunAsync("prompts rebase example");
        await f.RunAsync("prompts unknown");
        await f.RunAsync("prompts disable example");
        await f.RunAsync("prompts stop-use extra");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefinementCapturesOriginalEligibilityAndRetiresItOnCallChange(bool routed)
    {
        var f = await Fixture.CreateInitializedAsync();
        Func<bool>? captured = null;
        if (routed)
        {
            f.ViewModel.OwnedInstructionEditorRequested += (_, _) =>
            { captured = f.ViewModel.CaptureInstructionRefinementEligibility(); return Task.CompletedTask; };
            await f.RunAsync("instructions refine");
        }
        else { captured = f.ViewModel.CaptureInstructionRefinementEligibility(); }
        captured!().Should().BeTrue();
        f.CallState.SetState(CallState.Active);
        captured().Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefinementVoiceEntryRequiresCurrentNameAndPreservesOriginalChannel(bool prefixed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.OwnedInstructionEditorRequested += (operation, _) =>
        {
            operation.Should().Be("Refine");
            Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
            f.ViewModel.CaptureInstructionRefinementEligibility()().Should().BeTrue();
            calls++;
            return Task.CompletedTask;
        };
        await f.RaiseActivatedTranscriptAsync(prefixed ? "Kora, instructions refine" : "instructions refine", 1);
        calls.Should().Be(prefixed ? 1 : 0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task ExplicitUseDisablePreservesCapturedReasoningButRetiresNewCommandsAndPendingQuestions(ArtifactKind kind)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "ready");
        f.Reasoner.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await f.ViewModel.InitializeAsync();
        var artifact = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 1\nname: example\ndescription: Test\n---\nCaptured immutable content", false),
            kind, "kora-profile", "example").Definition;
        f.ArtifactCatalogue.Publish([artifact]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        await f.RunAsync("/" + kind.ToString().ToLowerInvariant() + " example");
        var reasoning = f.ViewModel.ActiveReasoningTask!;
        f.Reasoner.LastArtifact!.Instructions.Should().Be(artifact.Content);
        f.ArtifactCatalogue.Update(items => items.Select(item =>
            item with { IsUseEnabled = false, PreservesCapturedUse = true }).ToArray());
        f.ViewModel.CommandText = "/";
        f.ViewModel.ArtifactCommandOptions.Should().BeEmpty();
        f.Reasoner.Gate.SetResult("Finished with captured content");
        await reasoning;
        f.ViewModel.ResponseBody.Should().Contain("Finished with captured content");
        f.Reasoner.Requests.Should().ContainSingle();
        await f.RunAsync("/" + kind.ToString().ToLowerInvariant() + " example");
        f.Reasoner.Requests.Should().ContainSingle();
        f.ArtifactCatalogue.Publish([artifact]);
        f.Reasoner.Gate = null;
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.RunAsync("/" + kind.ToString().ToLowerInvariant() + " example");
        await f.ViewModel.ActiveReasoningTask!;
        var choice = f.ViewModel.ModelQuestionChoices[0];
        f.ArtifactCatalogue.Publish([artifact with { IsUseEnabled = false, PreservesCapturedUse = true }]);
        f.ViewModel.IsModelQuestionPending.Should().BeFalse();
        await f.ViewModel.SelectModelQuestionChoiceAsync(choice);
        f.Reasoner.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("action")]
    public async Task DisableWhileReasoningCannotPublishANewUnadmittedChoice(string response)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "ready");
        f.Reasoner.DecisionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (response is "question") { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        else { f.Reasoner.Action = BuiltInAction.LockMachine; }
        await f.ViewModel.InitializeAsync();
        var artifact = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 1\nname: example\ndescription: Test\n---\nCaptured content", false),
            ArtifactKind.Instruction, "kora-profile", "example").Definition;
        f.ArtifactCatalogue.Publish([artifact]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        await f.RunAsync("/instruction example");
        var reasoning = f.ViewModel.ActiveReasoningTask!;
        f.ArtifactCatalogue.Publish([artifact with { IsUseEnabled = false, PreservesCapturedUse = true }]);
        f.Reasoner.DecisionGate.SetResult();
        await reasoning;
        f.ViewModel.IsModelQuestionPending.Should().BeFalse();
        f.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Be("Artifact use disabled.");
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().ContainSingle();
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
