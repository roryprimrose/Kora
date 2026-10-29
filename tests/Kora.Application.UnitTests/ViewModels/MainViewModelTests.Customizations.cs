using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Customizations;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static CustomizationVoiceTarget EditorVoiceTarget(Func<bool>? current = null) =>
        new(new(CustomizationKind.Instruction, CustomizationScope.Kora, "example", "example.instructions.md", false, false),
            current ?? (static () => true));

    [Theory]
    [InlineData("success")]
    [InlineData("retired")]
    [InlineData("replaced")]
    [InlineData("unbound")]
    [InlineData("throw")]
    [InlineData("exact")]
    [InlineData("ownership")]
    public async Task Activated_editor_speech_never_enters_generic_reasoning_and_revalidates_the_original_target(string state)
    {
        var f = await Fixture.CreateInitializedAsync();
        var target = EditorVoiceTarget();
        f.ViewModel.PublishCustomizationVoiceTarget(target);
        var calls = 0;
        if (state is not "unbound")
        {
            f.ViewModel.BindCustomizationVoice(request =>
            {
                request.Target.Should().BeSameAs(target);
                request.Text.Should().Be("make this clearer");
                request.IsCurrent.Should().BeTrue();
                if (state is "ownership")
                {
                    f.ViewModel.BindVoiceOwnershipGate(static () => false);
                    request.IsCurrent.Should().BeFalse();
                }
                target.Retire();
                request.IsCurrent.Should().BeFalse();
                calls++;
                return state is "throw" ? Task.FromException(new IOException("Synthetic editor failure")) : Task.CompletedTask;
            });
        }
        f.Voice.AfterStart = () =>
        {
            if (state is "retired") { target.Retire(); }
            if (state is "replaced") { f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget()); }
        };
        Func<Task> action = () => f.RaiseActivatedTranscriptAsync(state is "exact" ? "refine this artifact make this clearer" : "make this clearer", 1);
        if (state is "throw") { await action.Should().ThrowAsync<IOException>(); }
        else { await action(); }
        calls.Should().Be(state is "success" or "throw" or "exact" or "ownership" ? 1 : 0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.IsBusy.Should().BeFalse();
        f.ViewModel.BindCustomizationVoice(null);
        f.ViewModel.PublishCustomizationVoiceTarget(null);
    }

    [Fact]
    public async Task Editor_opened_after_activation_cannot_claim_the_utterance_and_an_unbound_reference_cannot_start_a_session()
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.BindCustomizationVoice(_ => { calls++; return Task.CompletedTask; });
        f.Voice.AfterStart = () => f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget());
        await f.RaiseActivatedTranscriptAsync("make this instruction clearer", 1);
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.ResponseBody.Should().Contain("No session received");
        await f.RaiseActivatedTranscriptAsync("refine this artifact make this clearer", 1);
        calls.Should().Be(0, "replacement during capture retires the original target rather than rebinding it");
    }

    [Theory]
    [InlineData("what version are you running")]
    [InlineData("open settings")]
    [InlineData("show skills")]
    [InlineData("create kora adaptation adapted")]
    [InlineData("save skill adaptation")]
    [InlineData("open new request")]
    [InlineData("get appearance.presence-size")]
    [InlineData("use dark mode")]
    [InlineData("set speech rate to 1")]
    [InlineData("preview clipboard")]
    [InlineData("preview file")]
    [InlineData("list capabilities")]
    [InlineData("/instruction missing")]
    [InlineData("owned skill list")]
    [InlineData("agent author list")]
    [InlineData("independent agents")]
    [InlineData("instructions request cancel")]
    public async Task Explicit_local_routes_are_not_interpreted_as_editor_refinement(string command)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget());
        var calls = 0;
        f.ViewModel.BindCustomizationVoice(_ => { calls++; return Task.CompletedTask; });
        await f.RaiseActivatedTranscriptAsync(command.StartsWith('/') ? command : "Kora, " + command, 1);
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("topology")]
    [InlineData("recovery")]
    [InlineData("call")]
    [InlineData("call-revision")]
    [InlineData("owner")]
    [InlineData("owner-binding")]
    public async Task Foreground_context_observation_is_not_bound_to_a_previous_voice_generation_and_retires_on_host_change(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var current = f.ViewModel.CaptureCustomizationVoiceEligibility();
        current().Should().BeTrue();
        f.Voice.AdvanceGeneration();
        current().Should().BeTrue("a new foreground observation must not inherit the preceding request's voice generation");
        switch (change)
        {
            case "host": f.Session.IsUnlocked = false; break;
            case "topology":
                f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 10 };
                break;
            case "recovery": await f.ViewModel.SetVoiceConsentAsync(false); break;
            case "call": f.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
            case "call-revision": f.CallState.SetState(Kora.Core.Communication.CallState.Clear); break;
            case "owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); break;
            case "owner-binding":
                f.ViewModel.BindVoiceOwnershipGate(static () => false);
                f.ViewModel.BindVoiceOwnershipGate(static () => true);
                break;
        }
        current().Should().BeFalse();
    }

    [Theory]
    [InlineData("show skills", "List")]
    [InlineData("edit example instruction", "Select")]
    [InlineData("add an agent named example", "Create")]
    [InlineData("disable example MCP server", "Disable")]
    [InlineData("refine this artifact Explain this", "Refine")]
    [InlineData("create kora adaptation adapted", "Adapt")]
    [InlineData("save skill adaptation", "SaveAdaptation")]
    public async Task Customization_commands_use_the_shared_native_route_without_reasoning(string text, string operation)
    {
        var f = await Fixture.CreateInitializedAsync();
        CustomizationCommand? observed = null;
        f.ViewModel.CustomizationRequested += command =>
        { command.IsCurrent().Should().BeTrue(); observed = command; return Task.CompletedTask; };
        await f.RunAsync(text);
        observed!.Operation.Should().Be(operation);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdaptationCommandAuthorityRetainsOriginalInputChannel(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        var invoked = false;
        f.ViewModel.CustomizationRequested += command =>
        {
            invoked = true;
            command.Operation.Should().Be("SaveAdaptation");
            command.IsCurrent().Should().BeTrue();
            f.Voice.AdvanceGeneration();
            command.IsCurrent().Should().Be(!voice);
            return Task.CompletedTask;
        };
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, save skill adaptation", 1); }
        else { await f.RunAsync("save skill adaptation"); }
        invoked.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_customization_window_is_explicit_and_dirty_shutdown_remains_blocking()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("show customizations");
        f.ViewModel.ResponseBody.Should().Contain("unavailable");
        f.Reasoner.Requests.Should().BeEmpty();
        var calls = 0;
        f.ViewModel.BindCustomizationShutdown(() => { calls++; return Task.FromResult(false); });
        await f.ViewModel.ExitAsync();
        (await f.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        calls.Should().Be(2);
        f.ViewModel.BindCustomizationShutdown(() => Task.FromResult(true));
        (await f.ViewModel.TryPrepareHandoffAsync()).Should().BeTrue();
        f.ViewModel.BindCustomizationShutdown(null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Customization_guard_remains_authoritative_for_application_restart(bool accepted)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindCustomizationShutdown(() => Task.FromResult(accepted));
        await f.RunAsync("restart the app");
        f.Events.Contains("process.restart").Should().Be(accepted);
    }

    [Fact]
    public async Task Unavailable_assistant_name_cannot_route_prefixed_customizations_but_unprefixed_recovery_remains()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var dispatched = 0;
        f.ViewModel.CustomizationRequested += _ => { dispatched++; return Task.CompletedTask; };
        await f.RunAsync("Kora, show customizations");
        dispatched.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Contain("unavailable");
        await f.RunAsync("show customizations");
        dispatched.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
