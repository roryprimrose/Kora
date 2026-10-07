using AwesomeAssertions;
using Kora.Application.Maintenance;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public void Maintenance_entry_projects_passive_status_without_changing_existing_version_question()
    {
        var fixture = new Fixture();
        fixture.ViewModel.MaintenanceStatus.Should().Contain("Unknown");
        fixture.ViewModel.ShowMaintenance();
        var opened = 0;
        fixture.ViewModel.MaintenanceRequested += (_, _) => opened++;
        fixture.ViewModel.ShowMaintenance();
        opened.Should().Be(1);
        fixture.ViewModel.MaintenanceStatus = "Available: unsigned canonical metadata";
        fixture.ViewModel.MaintenanceStatus.Should().Contain("Available");
    }

    [Theory]
    [InlineData("maintenance status", MaintenanceCommand.Status)]
    [InlineData("Kora, maintenance review", MaintenanceCommand.Review)]
    [InlineData("maintenance snooze", MaintenanceCommand.Snooze)]
    public async Task Exact_typed_cached_commands_are_local_visual_and_do_not_reason_or_speak(string text, MaintenanceCommand expected)
    {
        var f = await Fixture.CreateInitializedAsync();
        var commands = new CachedCommands();
        f.ViewModel.BindMaintenanceCommands(commands);
        f.TextToSpeech.ClearSpokenResponse();
        f.ViewModel.DefaultResponseMode = Kora.Core.Voice.ResponseOutputMode.VoiceOnly;
        var opened = 0;
        f.ViewModel.MaintenanceRequested += (_, _) => opened++;
        await f.RunAsync(text);
        commands.Command.Should().Be(expected);
        commands.Origin.Should().Be(RequestOrigin.LocalUi);
        f.ViewModel.ResponseBody.Should().Be("Exact complete cached maintenance response.");
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        opened.Should().Be(expected == MaintenanceCommand.Review ? 1 : 0);
    }

    [Fact]
    public async Task Activated_route_retains_original_channel_and_exact_recognition_phrases_without_provider_output()
    {
        var f = await Fixture.CreateInitializedAsync();
        var commands = new CachedCommands();
        f.ViewModel.BindMaintenanceCommands(commands);
        f.TextToSpeech.ClearSpokenResponse();
        await f.RaiseActivatedTranscriptAsync("Kora, maintenance status", 1);
        commands.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        commands.Command.Should().Be(MaintenanceCommand.Status);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("maintenance check")]
    [InlineData("maintenance status appended")]
    [InlineData("maintenance review https://example.com")]
    [InlineData("maintenance snooze approval")]
    public async Task Invalid_exact_maintenance_syntax_never_reaches_model_or_native_service(string text)
    {
        var f = await Fixture.CreateInitializedAsync();
        var commands = new CachedCommands();
        f.ViewModel.BindMaintenanceCommands(commands);
        await f.RunAsync(text);
        commands.Command.Should().BeNull();
        f.ViewModel.ResponseBody.Should().Contain(MaintenanceCommandParser.Syntax);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    public async Task Maintenance_is_not_an_answer_and_preserves_complete_pending_question_or_approval(string pending)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "ready");
        if (pending is "question") { f.Reasoner.Question = new("Which exact option?", ["One", "Two"]); }
        else { f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine; }
        await f.ViewModel.InitializeAsync();
        if (pending is "grant")
        {
            await f.RunAsync("allow models to lock the machine this session");
        }
        else
        {
            await f.RunAsync("please help with this workstation");
            await f.ViewModel.ActiveReasoningTask!;
        }
        var preview = f.ViewModel.ResponseBody;
        var title = f.ViewModel.ResponseTitle;
        var commands = new CachedCommands();
        f.ViewModel.BindMaintenanceCommands(commands);
        f.ViewModel.CanRunMaintenanceCommands.Should().BeFalse();
        await f.RunAsync("maintenance snooze");
        commands.Command.Should().BeNull();
        f.ViewModel.ResponseBody.Should().Be(preview);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.Transcript.Should().Contain("remains unchanged");
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.System)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    public async Task Non_user_initiators_cannot_be_relabelled_as_local_UI(SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var commands = new CachedCommands();
        f.ViewModel.BindMaintenanceCommands(commands);
        await f.ViewModel.ExecuteMaintenanceCommandAsync(MaintenanceCommand.Status, initiator);
        commands.Command.Should().BeNull();
        f.ViewModel.ResponseTitle.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task Protected_or_unknown_call_denies_cached_commands_without_hidden_later_execution(CallState state)
    {
        var f = await Fixture.CreateInitializedAsync();
        var commands = new CachedCommands();
        f.ViewModel.BindMaintenanceCommands(commands);
        f.CallState.SetState(state);
        await f.RunAsync("maintenance status");
        commands.Command.Should().BeNull();
        f.ViewModel.CanRunMaintenanceCommands.Should().BeFalse();
        f.CallState.SetState(CallState.Clear);
        commands.Command.Should().BeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("failed")]
    [InlineData("late-call")]
    [InlineData("late-privacy")]
    [InlineData("late-dispose")]
    [InlineData("late-voice")]
    [InlineData("late-topology")]
    public async Task Missing_failure_or_changed_host_is_not_a_cached_success_or_automatic_retry(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        var commands = new CachedCommands();
        if (boundary is not "missing") { f.ViewModel.BindMaintenanceCommands(commands); }
        commands.BeforeReturn = () =>
        {
            switch (boundary)
            {
                case "failed": throw new IOException("fixture");
                case "late-call": f.CallState.SetState(CallState.Active); break;
                case "late-privacy": f.Session.IsUnlocked = false; break;
                case "late-dispose": f.ViewModel.Dispose(); break;
                case "late-voice": typeof(Kora.Application.ViewModels.MainViewModel)
                    .GetField("voiceEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(f.ViewModel, 0); break;
                case "late-topology": f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                    { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 }; break;
            }
        };
        if (boundary is "late-voice")
        {
            await f.RaiseActivatedTranscriptAsync("Kora, maintenance status", 1);
        }
        else { await f.RunAsync("maintenance status"); }
        f.ViewModel.ResponseBody.Should().NotBe("Exact complete cached maintenance response.");
        if (boundary is "missing" or "failed" or "late-call" or "late-topology")
        {
            (f.ViewModel.ResponseTitle.Contains("unavailable", StringComparison.Ordinal)
                || f.ViewModel.ResponseTitle.Contains("not confirmed", StringComparison.Ordinal)).Should().BeTrue();
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Disposed_host_does_not_dispatch_or_present()
    {
        var f = await Fixture.CreateInitializedAsync();
        var commands = new CachedCommands();
        f.ViewModel.BindMaintenanceCommands(commands);
        f.ViewModel.Dispose();
        await f.ViewModel.ExecuteMaintenanceCommandAsync(MaintenanceCommand.Status, SecurityAuditInitiator.LocalUser);
        commands.Command.Should().BeNull();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("call-visual-off")]
    [InlineData("no-window")]
    public async Task Visual_fallback_respects_current_privacy_call_output_and_absent_subscriber(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        if (boundary is "private") { f.Session.IsUnlocked = false; }
        if (boundary is "call-visual-off")
        {
            f.CallState.SetState(CallState.Active);
            await f.ViewModel.SetShowVisualTextDuringCallsAsync(false);
        }
        if (boundary is "no-window")
        {
            typeof(Kora.Application.ViewModels.MainViewModel)
                .GetField("WindowActionRequested", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(f.ViewModel, null);
        }
        await f.ViewModel.ExecuteMaintenanceCommandAsync(MaintenanceCommand.Invalid, SecurityAuditInitiator.LocalUser);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    private sealed class CachedCommands : IMaintenanceCommands
    {
        internal MaintenanceCommand? Command;
        internal RequestOrigin Origin;
        internal Action? BeforeReturn;
        public Task<string> ExecuteAsync(MaintenanceCommand command, RequestOrigin origin, Func<bool> eligible,
            CancellationToken cancellationToken)
        {
            eligible().Should().BeTrue();
            Command = command;
            Origin = origin;
            BeforeReturn?.Invoke();
            return Task.FromResult("Exact complete cached maintenance response.");
        }
    }
}
