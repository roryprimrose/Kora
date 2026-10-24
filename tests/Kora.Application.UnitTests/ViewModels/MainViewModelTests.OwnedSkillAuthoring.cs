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
