using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservationWorkflowAuthoringAndReviewCommandsRetainOriginalTypedOrActivatedVoiceAuthority(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.LocalUser);
        var captured = new List<string>();
        f.ViewModel.BindOwnedSkillAuthoring((command, authority) =>
        {
            command.Operation.Should().Be("workflow");
            authority.IsCurrent().Should().BeTrue();
            authority.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            captured.Add(command.Literal!);
            return Task.CompletedTask;
        }, null);
        foreach (var command in new[] { "create example 1.0.0", "definition: {\"steps\":[{\"tool\":\"application.get_version\",\"input\":{}}]}",
            "validate", "save", "targets", "request: unchanged original", "review example 1.0.0", "enable", "invoke", "approve-step", "status", "cancel" })
        {
            var text = "owned skill workflow " + command;
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        captured.Should().HaveCount(12);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalNewSkillRequestAndClarificationsUseOriginalAuthoringRoute(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.LocalUser);
        var captured = new List<OwnedSkillAuthoringCommand>();
        f.ViewModel.BindOwnedSkillAuthoring((command, authority) =>
        {
            authority.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            captured.Add(command);
            return Task.CompletedTask;
        }, null);
        foreach (var command in new[] { "create a skill that  explains failures\r\nowned skill save  ",
            "owned skill expected-input:  user text  ", "owned skill expected-output:  explanation  ", "owned skill generate" })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + command, 1); }
            else { await f.RunAsync(command); }
        }
        captured.Select(command => command.Operation).Should().Equal("generate-request", "expected-input", "expected-output", "generate");
        captured[0].Literal.Should().Be(" explains failures\r\nowned skill save  ");
        captured[1].Literal.Should().Be(" user text  ");
        captured[2].Literal.Should().Be(" explanation  ");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSkillAuthoringPreservesOriginalTypedOrCurrentNameVoiceAndLiteralPayload(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.LocalUser);
        var captured = new List<(OwnedSkillAuthoringCommand Command, OwnedSkillAuthoringAuthority Authority)>();
        f.ViewModel.BindOwnedSkillAuthoring((command, authority) =>
        {
            authority.IsCurrent().Should().BeTrue();
            captured.Add((command, authority));
            return Task.CompletedTask;
        }, null);
        var input = "owned skill guidance: owned skill save";
        if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + input, 1); }
        else { await f.RunAsync(input); }
        captured.Should().ContainSingle();
        captured[0].Command.Operation.Should().Be("guidance");
        captured[0].Command.Literal.Should().Be("owned skill save");
        captured[0].Authority.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSkillCritiqueAndRefineUseOriginalTypedOrActivatedVoiceAuthority(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.LocalUser);
        var captured = new List<OwnedSkillAuthoringCommand>();
        f.ViewModel.BindOwnedSkillAuthoring((command, authority) =>
        {
            authority.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            captured.Add(command);
            return Task.CompletedTask;
        }, null);
        foreach (var command in new[] { "owned skill critique:  unchanged  ", "owned skill refine", "owned skill cancel-refinement" })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + command, 1); }
            else { await f.RunAsync(command); }
        }
        captured.Select(command => command.Operation).Should().Equal("critique", "refine", "cancel-refinement");
        captured[0].Literal.Should().Be(" unchanged  ");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owned skill run example")]
    [InlineData("owned skill create CON 1.0.0")]
    [InlineData("owned skill save now")]
    public async Task MalformedOwnedSkillCommandsNeverFallThroughToModels(string input)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.BindOwnedSkillAuthoring((_, _) => { calls++; return Task.CompletedTask; }, null);
        await f.RunAsync(input);
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task OwnedSkillDirtyCancelBlocksExitAndHandoffWithoutReplacingInstructionOrConversationHooks()
    {
        var f = await Fixture.CreateInitializedAsync();
        var instruction = 0;
        f.ViewModel.BindInstructionEditorShutdown(() => { instruction++; return Task.FromResult(true); });
        f.ViewModel.BindOwnedSkillAuthoring(null, () => Task.FromResult(false));
        await f.ViewModel.ExitAsync();
        (await f.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        instruction.Should().Be(0);
        f.ViewModel.BindOwnedSkillAuthoring(null, null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSkillRestartDecisionPreservesExistingShutdownGatesAndPreventsUnapprovedDispatch(bool allow)
    {
        var f = await Fixture.CreateInitializedAsync();
        var owned = 0;
        var conversation = 0;
        var instruction = 0;
        f.ViewModel.BindOwnedSkillAuthoring(null, () => { owned++; return Task.FromResult(allow); });
        f.ViewModel.BindConversationShutdown(() => { conversation++; return Task.FromResult(true); }, static () => true);
        f.ViewModel.BindInstructionEditorShutdown(() => { instruction++; return Task.FromResult(true); });
        await f.RunAsync("restart the app");
        owned.Should().Be(1);
        conversation.Should().Be(allow ? 1 : 0);
        instruction.Should().Be(allow ? 1 : 0);
        if (allow) { f.Events.Should().Contain("process.restart"); }
        else { f.Events.Should().NotContain("process.restart"); }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("system")]
    [InlineData("binding")]
    [InlineData("unprefixed-voice")]
    [InlineData("prefix-unavailable")]
    public async Task OwnedSkillAuthoringMissingRetiredOrNonOriginalAuthorityNeverDispatches(string change)
    {
        var f = new Fixture();
        if (change is "prefix-unavailable") { f.NamePreferences.Name = "Supported Commands"; }
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        if (change is not "missing")
        { f.ViewModel.BindOwnedSkillAuthoring((_, _) => { calls++; return Task.CompletedTask; }, null); }
        if (change is "binding") { f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindOwnedSkillAuthoring(null, null); }
        using var root = change is "system"
            ? Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request) : null;
        if (change is "unprefixed-voice") { await f.RaiseActivatedTranscriptAsync("owned skill list", 1); }
        else { await f.RunAsync(change is "prefix-unavailable" ? "Kora, owned skill list" : "owned skill list"); }
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
