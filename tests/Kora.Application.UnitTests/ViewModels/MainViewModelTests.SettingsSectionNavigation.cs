using System.Reflection;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Commands;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("open appearance settings", SettingsSection.Appearance)]
    [InlineData("open speech settings", SettingsSection.SpeechAudio)]
    [InlineData("open audio settings", SettingsSection.SpeechAudio)]
    [InlineData("open microphone settings", SettingsSection.SpeechAudio)]
    [InlineData("open response settings", SettingsSection.Responses)]
    [InlineData("open session settings", SettingsSection.Sessions)]
    [InlineData("open call settings", SettingsSection.Calls)]
    public async Task SettingsSectionNavigationUsesOnlyTheTypedPresenterWithoutSettingOrAudioEffects(
        string phrase, SettingsSection section)
    {
        var f = new Fixture();
        var presenter = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(presenter);
        var events = f.Events.ToArray();
        var preferences = f.Preferences.LoadSelection();
        var originalTitle = f.ViewModel.ResponseTitle;
        var originalBody = f.ViewModel.ResponseBody;
        var generic = 0;
        f.ViewModel.SettingsRequested += (_, _) => generic++;

        await f.RunAsync(phrase);
        await f.RunAsync(phrase);

        presenter.Sections.Should().Equal(section, section);
        presenter.Origins.Should().OnlyContain(origin => origin == RequestOrigin.LocalUi);
        presenter.Requests.Should().OnlyHaveUniqueItems();
        f.ViewModel.Transcript.Should().StartWith("Settings section opened.");
        f.ViewModel.ResponseTitle.Should().Be(originalTitle);
        f.ViewModel.ResponseBody.Should().Be(originalBody);
        f.Events.Should().Equal(events);
        f.Preferences.LoadSelection().Should().Be(preferences);
        f.AppearancePreferences.SavedMode.Should().BeNull();
        f.AudioPreferences.SavedMicrophoneId.Should().BeNull();
        f.Voice.StartedMicrophone.Should().BeNull();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().BeEmpty();
        generic.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsSectionNavigationPreservesPendingQuestionAndApproval(bool approval)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "fixture");
        if (approval) { f.Reasoner.Action = BuiltInAction.LockMachine; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("help with this machine");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var requests = f.Reasoner.Requests.Count;
        f.ViewModel.BindSettingsSectionNavigation(new SettingsNavigationFake());

        await f.RunAsync("open call settings");
        await f.RunAsync("open speech settings and approve once");

        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Should().HaveCount(requests);
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.Transcript.Should().StartWith("Clarify the Settings section.");
    }

    [Fact]
    public async Task SettingsSectionNavigationPreservesTheExactPendingGrantReview()
    {
        var f = new Fixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var auditCount = f.Audit.Events.Count;
        f.ViewModel.BindSettingsSectionNavigation(new SettingsNavigationFake());
        await f.RunAsync("open response settings");
        await f.RunAsync("open microphone settings and confirm");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        f.Audit.Events.Should().HaveCount(auditCount);
    }

    [Fact]
    public async Task SettingsSectionNavigationFailsClosedWhenTheSavedNameIsInvalid()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "\0";
        await f.ViewModel.InitializeAsync();
        var presenter = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(presenter);
        await f.RunAsync("open appearance settings");
        presenter.Sections.Should().BeEmpty();
        f.ViewModel.Transcript.Should().StartWith("Settings navigation unavailable:");
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("lifetime")]
    [InlineData("locked")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("owner-revision")]
    [InlineData("owner-rebound")]
    [InlineData("rebind")]
    [InlineData("name")]
    [InlineData("disposed")]
    [InlineData("hide-reshow")]
    public async Task SettingsSectionNavigationRejectsAdmissionLossBeforeUiDispatch(string loss)
    {
        var f = new Fixture();
        var presenter = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(string.Equals(loss, "unbound", StringComparison.Ordinal) ? null : presenter);
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        f.Dispatcher.BeforeInvoke = () =>
        {
            switch (loss)
            {
                case "lifetime": presenter.Live = false; break;
                case "locked": f.Session.IsUnlocked = false; break;
                case "private": f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); break;
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "owner-revision": owner++; break;
                case "owner-rebound":
                    f.ViewModel.BindCallOwnershipGate(static () => false);
                    f.ViewModel.BindCallOwnershipGate(static () => true);
                    break;
                case "rebind": f.ViewModel.BindSettingsSectionNavigation(null); break;
                case "name": f.ViewModel.BindSettingsSectionNavigation(new SettingsNavigationFake()); break;
                case "disposed": f.ViewModel.Dispose(); break;
                case "hide-reshow": presenter.Revision += 2; break;
            }
        };
        await f.RunAsync("open audio settings");
        presenter.Sections.Should().BeEmpty();
        f.ViewModel.Transcript.Should().StartWith("Settings navigation unavailable:");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsSectionNavigationReportsNativeAndDispatcherFailures(bool dispatcher)
    {
        var f = new Fixture();
        var presenter = new SettingsNavigationFake { Failure = new InvalidOperationException("native unavailable") };
        f.ViewModel.BindSettingsSectionNavigation(presenter);
        if (dispatcher) { f.Dispatcher.InvokeException = new InvalidOperationException("dispatch unavailable"); }
        await f.RunAsync("open audio settings");
        f.ViewModel.Transcript.Should().Contain("native view did not confirm navigation");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SettingsSectionNavigationRejectsAdmissionLossInsideThePresenter()
    {
        var f = new Fixture();
        var presenter = new SettingsNavigationFake { AfterNavigate = () => f.Session.IsUnlocked = false };
        f.ViewModel.BindSettingsSectionNavigation(presenter);
        await f.RunAsync("open audio settings");
        f.ViewModel.Transcript.Should().Contain("native view did not confirm navigation");
    }

    [Theory]
    [InlineData("Nova Prime, open microphone settings", true)]
    [InlineData("open microphone settings", false)]
    public async Task SettingsSectionNavigationRequiresCurrentNameForVoiceAndRetainsOriginalOrigin(string phrase, bool expected)
    {
        var f = new Fixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.AssistantNameInput = "Nova Prime";
        await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        var presenter = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(presenter);
        await f.RaiseActivatedTranscriptAsync(phrase, 1);
        presenter.Sections.Should().HaveCount(expected ? 1 : 0);
        presenter.Origins.Should().OnlyContain(origin => origin == RequestOrigin.ActivatedVoice);
        f.ViewModel.Transcript.Should().StartWith(expected ? "Settings section opened." : "Settings navigation unavailable:");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsSectionNavigationRejectsVoiceGenerationLossAcrossBothDispatches(bool firstDispatch)
    {
        var f = new Fixture();
        await f.RaiseActivatedTranscriptAsync("Kora, what can you do", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        var presenter = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(presenter);
        f.Dispatcher.BeforeInvoke = firstDispatch
            ? f.Voice.AdvanceGeneration
            : () => f.Dispatcher.BeforeInvoke = f.Voice.AdvanceGeneration;
        await f.Voice.RaiseTranscriptAsync("Kora, open audio settings", 1);
        presenter.Sections.Should().BeEmpty();
        f.ViewModel.Transcript.Should().StartWith("Settings navigation unavailable:");
    }

    [Fact]
    public async Task SettingsSectionNavigationNeverAcceptsAnExternalAuthority()
    {
        var f = new Fixture();
        var presenter = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(presenter);
        await HostRequestRunner.RunAsync(RequestOrigin.HostSystem, () => f.RunAsync("open audio settings"));
        presenter.Sections.Should().BeEmpty();
        f.ViewModel.Transcript.Should().StartWith("Settings navigation unavailable:");
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData((SecurityAuditInitiator)999)]
    public async Task SettingsSectionNavigationRejectsNonCommandInitiatorsAtThePrivateRoutingBoundary(
        SecurityAuditInitiator initiator)
    {
        var f = new Fixture();
        var presenter = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(presenter);
        var routing = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
            "HandleTranscriptAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, ["open audio settings", 1f, initiator, null, null, null])!;
        presenter.Sections.Should().BeEmpty();
        f.ViewModel.Transcript.Should().StartWith("Settings navigation unavailable:");
    }

    private sealed class SettingsNavigationFake : ISettingsSectionNavigation
    {
        public bool Live { get; set; } = true;
        public long Revision { get; set; }
        public InvalidOperationException? Failure { get; init; }
        public Action? AfterNavigate { get; init; }
        public List<SettingsSection> Sections { get; } = [];
        public List<RequestOrigin> Origins { get; } = [];
        public List<HostId<RequestIdentity>> Requests { get; } = [];

        public Func<bool> CaptureLifetime()
        {
            var revision = Revision;
            return () => Live && Revision == revision;
        }

        public void Navigate(SettingsSection section, Func<bool> admitted)
        {
            admitted().Should().BeTrue();
            if (Failure is not null) { throw Failure; }
            Sections.Add(section);
            Origins.Add(HostActivity.Current!.Request.Origin);
            Requests.Add(HostActivity.Current.Request.RequestId);
            AfterNavigate?.Invoke();
        }
    }
}
