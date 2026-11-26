using AwesomeAssertions;

using Kora.Core.Communication;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Commands;

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
    [InlineData("instructions validate", "Validate", null)]
    [InlineData("Kora, instructions validate", "Validate", null)]
    [InlineData("instructions compare", "Compare", null)]
    [InlineData("Kora, instructions compare", "Compare", null)]
    [InlineData("instructions rebase", "Rebase", null)]
    [InlineData("Kora, instructions rebase", "Rebase", null)]
    [InlineData("instructions restore", "Restore previous saved content", null)]
    [InlineData("Kora, instructions restore", "Restore previous saved content", null)]
    [InlineData("instructions refine", "Refine", null)]
    [InlineData("Kora, instructions refine", "Refine", null)]
    [InlineData("instructions copy new-copy", "Save as new copy", "new-copy")]
    [InlineData("instructions rename new-id", "Rename", "new-id")]
    [InlineData("Kora, instructions rename new-id", "Rename", "new-id")]
    [InlineData("instructions recover-rename", "Recover rename", null)]
    [InlineData("Kora, instructions recover-rename", "Recover rename", null)]
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
        await f.RunAsync("instructions rename");
        await f.RunAsync("instructions rename new-id extra");
        await f.RunAsync("instructions recover-rename extra");
        await f.RunAsync("prompts refine");
        await f.RunAsync("prompts rename new-id");
        await f.RunAsync("prompts recover-rename");
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
        await f.RunAsync("prompts restore");
        await f.RunAsync("instructions restore example");
        await f.RunAsync("prompts unknown");
        await f.RunAsync("prompts disable example");
        await f.RunAsync("prompts stop-use extra");

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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RefinementCapturesOriginalEligibilityAndRetiresItOnCallChange(bool routed, bool restore)
    {
        var f = await Fixture.CreateInitializedAsync();
        Func<bool>? captured = null;
        if (routed)
        {
            f.ViewModel.OwnedInstructionEditorRequested += (_, _) =>
            { captured = restore ? f.ViewModel.CaptureInstructionRestoreEligibility() : f.ViewModel.CaptureInstructionRefinementEligibility(); return Task.CompletedTask; };
            await f.RunAsync(restore ? "instructions restore" : "instructions refine");
        }
        else { captured = restore ? f.ViewModel.CaptureInstructionRestoreEligibility() : f.ViewModel.CaptureInstructionRefinementEligibility(); }
        captured!().Should().BeTrue();
        f.CallState.SetState(CallState.Active);
        captured().Should().BeFalse();

    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RefinementVoiceEntryRequiresCurrentNameAndPreservesOriginalChannel(bool prefixed, bool restore)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.OwnedInstructionEditorRequested += (operation, _) =>
        {
            operation.Should().Be(restore ? "Restore previous saved content" : "Refine");
            Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
            var eligible = restore ? f.ViewModel.CaptureInstructionRestoreEligibility() : f.ViewModel.CaptureInstructionRefinementEligibility();
            eligible().Should().BeTrue();
            calls++;
            return Task.CompletedTask;
        };
        await f.RaiseActivatedTranscriptAsync((prefixed ? "Kora, " : "") + (restore ? "instructions restore" : "instructions refine"), 1);
        calls.Should().Be(prefixed ? 1 : 0);

    }

    [Theory]
    [InlineData("rename renamed", "Rename", false)]
    [InlineData("rename renamed", "Rename", true)]
    [InlineData("recover-rename", "Recover rename", false)]
    [InlineData("recover-rename", "Recover rename", true)]
    public async Task RenameVoiceEntryRequiresCurrentNameAndKeepsOriginalChannel(string input, string expected, bool prefixed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.OwnedInstructionEditorRequested += (operation, _) =>
        {
            operation.Should().Be(expected);
            Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
            f.ViewModel.CaptureInstructionRefinementEligibility()().Should().BeTrue();
            calls++;
            return Task.CompletedTask;
        };
        await f.RaiseActivatedTranscriptAsync((prefixed ? "Kora, " : "") + "instructions " + input, 1);
        calls.Should().Be(prefixed ? 1 : 0);

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

    }
}