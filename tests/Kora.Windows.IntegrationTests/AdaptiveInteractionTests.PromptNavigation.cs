using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Application.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationHistoryPromptCompletesAnExactBoundedReadWithoutAnotherSubmission(bool voice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture(voice);
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "retained exact history source";
            await f.Model.SubmitConversationAsync();
            var request = f.Model.ConversationMessages.Single().Request;
            f.Model.ComposerBody = "independent pending input";
            if (voice) { await f.RunVoice("show me the conversation history for this session"); }
            else { await f.Run("show me the conversation history for this session"); }
            f.Window!.IsExpanded.Should().BeTrue();
            f.Model.ConversationMessages.Should().ContainSingle().Which.Request.Should().Be(request);
            f.Model.ConversationStatus.Should().Contain("Observed 1");
            f.Model.ComposerBody.Should().Be("independent pending input");
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresenceSettingsPromptShowsAndFocusesTheExistingSubsectionWithoutChangingSettings(bool voice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture(voice);
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            var window = f.Window!;
            using var settings = new SettingsWindowController(f.Native.Main, NullLogger<SettingsWindowController>.Instance);
            settings.BindWorkbench(() => window, view => window.ShowHostedView(WorkbenchActivity.Settings, view));
            f.Commands.BindManagement(settings);
            f.Model.ComposerBody = "keep while inspecting presence";
            var display = f.Native.Main.IsPresenceDisplayEnabled;
            var timeout = f.Native.Main.PresenceTimeoutSeconds;
            if (voice) { await f.RunVoice("show me the settings for the Kora presence"); }
            else { await f.Run("show me the settings for the Kora presence"); }
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var view = (SettingsView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            view.FindControl<Kora.Controls.SettingsBrowser>("SettingsTabs")!.SelectedItem.Should().Be(view.FindControl<Kora.Controls.SettingsPage>("AppearanceTab"));
            view.FindControl<TextBlock>("PresenceHeading")!.IsFocused.Should().BeTrue();
            f.Native.Main.IsPresenceDisplayEnabled.Should().Be(display);
            f.Native.Main.PresenceTimeoutSeconds.Should().Be(timeout);
            f.Model.ComposerBody.Should().Be("keep while inspecting presence");
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task ThisSessionHistoryPromptRefusesMissingSelectionInsteadOfChoosingAnotherSession()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("show me the conversation history for this session");
            f.Model.SelectedSessionRecord.Should().BeNull();
            f.Model.ConversationMessages.Should().BeEmpty();
            f.Native.Main.ResponseBody.Should().Contain("select an exact session first");
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditHistoryPromptDisplaysBoundedCommittedAuditWithoutReadingDiagnosticsOrRetargetingChat(bool voice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture(voice);
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            var window = f.Window!;
            using var settings = new SettingsWindowController(f.Native.Main, NullLogger<SettingsWindowController>.Instance);
            using var evidence = new EvidenceWindowController(f.Native.Main, f.Native.Evidence, f.Native.Access,
                NullLogger<EvidenceViewModel>.Instance, f.Native.Access);
            evidence.Bind();
            evidence.BindWorkbench(() => window, view => window.ShowHostedView(WorkbenchActivity.Audit, view));
            f.Commands.BindManagement(settings, evidenceController: evidence);
            f.Model.ComposerBody = "independent audit inspection input";
            var speech = f.Native.Voice.StartCalls;
            if (voice) { await f.RunVoice("show me the audit history"); }
            else { await f.Run("show me the audit history"); }
            var view = (EvidenceView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            var model = (EvidenceViewModel)view.DataContext!;
            window.ActiveActivity.Should().Be(WorkbenchActivity.Audit);
            model.Source.Should().Be(EvidenceSource.AuthorityAudit);
            model.SessionFilter.Should().BeEmpty();
            model.Status.Should().Contain("Application-wide committed authority audit");
            model.Records.Should().NotBeEmpty().And.OnlyContain(record => record.Reference.Source == EvidenceSource.AuthorityAudit);
            model.ResultText.Should().NotBeEmpty();
            f.Model.ComposerBody.Should().Be("independent audit inspection input");
            f.Model.SelectedSessionRecord!.Authority.Should().Be(f.Conversation.Authority);
            f.Admission.Calls.Should().Be(0);
            if (!voice) { f.Native.Voice.StartCalls.Should().Be(speech); }
        });
    }

    [Fact]
    public async Task ActivatedVoiceSessionAuditRetainsTheExactSelectedScopeWithoutNativeChannelRelabelling()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture(voice: true);
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "one retained request for audit inspection";
            await f.Model.SubmitConversationAsync();
            var session = f.Model.SelectedSessionRecord!;
            await f.RunVoice("show session audit");
            f.Window!.SessionView.DetailsVisible.Should().BeTrue(f.Native.Main.ResponseBody);
            var result = f.Model.EvidenceDetails.Should().ContainSingle().Which;
            result.Source.Should().Be(EvidenceSource.AuthorityAudit);
            result.Page.Records.Should().NotBeEmpty().And.OnlyContain(record => record.Reference.Source == EvidenceSource.AuthorityAudit);
            f.Model.SelectedSessionRecord!.Authority.Should().Be(session.Authority);
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false, "input")]
    [InlineData(true, "input")]
    [InlineData(false, "hide-return")]
    [InlineData(true, "hide-return")]
    [InlineData(false, "minimize")]
    [InlineData(true, "minimize")]
    [InlineData(false, "privacy")]
    [InlineData(true, "privacy")]
    public async Task LateAuditPromptReadKeepsItsOriginalChannelAndCannotDiscloseAfterRetirement(bool voice, string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture(voice);
            await f.Initialize();
            await f.Run("show sessions");
            var window = f.Window!;
            var reader = new RequestAuditReader((ICommittedAuthorityAuditReader)f.Native.Interactions) { Hold = true };
            var query = new DurableEvidenceQuery(reader, f.Native.Access, f.Native.Time, NullLogger<DurableEvidenceQuery>.Instance);
            using var settings = new SettingsWindowController(f.Native.Main, NullLogger<SettingsWindowController>.Instance);
            using var evidence = new EvidenceWindowController(f.Native.Main, query, f.Native.Access,
                NullLogger<EvidenceViewModel>.Instance, f.Native.Access);
            evidence.Bind();
            evidence.BindWorkbench(() => window, view => window.ShowHostedView(WorkbenchActivity.Audit, view));
            f.Commands.BindManagement(settings, evidenceController: evidence);
            var command = voice ? f.RunVoice("show me the audit history") : f.Run("show me the audit history");
            await reader.Entered.Task;
            var view = (EvidenceView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            var model = (EvidenceViewModel)view.DataContext!;
            try
            {
                switch (boundary)
                {
                    case "input": f.Native.Main.CommandText = "different original request"; break;
                    case "hide-return": window.Hide(); f.Native.Main.ShowApplication(); break;
                    case "minimize": window.WindowState = WindowState.Minimized; break;
                    case "privacy": f.Native.Access.SetOpen(false); break;
                }
                command.IsCompleted.Should().BeFalse("withdrawn admission cannot release the original physical read");
                if (boundary is "input" or "hide-return" or "minimize")
                { reader.HeldCancellation.IsCancellationRequested.Should().BeTrue("known input or native withdrawal cancels the read but still joins it"); }
                model.Records.Should().BeEmpty();
                model.ResultText.Should().BeEmpty();
                reader.Requests.Should().ContainSingle().Which.Origin.Should().Be(
                    voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
                reader.Queries.Should().ContainSingle().Which.Source.Should().Be(EvidenceSource.AuthorityAudit);
            }
            finally { reader.Release.TrySetResult(); }
            await command;
            model.Records.Should().BeEmpty();
            model.ResultText.Should().BeEmpty();
            model.Status.Should().NotContain("Application-wide committed authority audit");
            f.Admission.Calls.Should().Be(0);
        });
    }
}
