using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Interaction;
using Kora.Core.Diagnostics;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(true, "native", false)]
    [InlineData(false, "native", true)]
    [InlineData(true, "typed", true)]
    [InlineData(false, "typed", false)]
    [InlineData(true, "activated", false)]
    [InlineData(false, "activated", true)]
    [InlineData(false, "expiry", false)]
    [InlineData(false, "hide", false)]
    [InlineData(false, "composer", false)]
    [InlineData(false, "preference", false)]
    [InlineData(false, "restart", false)]
    [InlineData(false, "retention", false)]
    [InlineData(false, "unrelated-key", false)]
    [InlineData(false, "native-failure", false)]
    [InlineData(false, "higher-priority", false)]
    public async Task OptedOriginalRequestCreatesIndependentQuestionAndNativeAnswerRevealsOnlyExactResult(
        bool yes, string route, bool voiceOriginal)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), syntheticVoice: true);
            await using var markdownAdmission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            await using var markdown = new MarkdownDisplayConfigurationService(new MarkdownDisplaySettingsTests.Preferences(),
                markdownAdmission, new AuditLog(), f.Time, NullLogger<MarkdownDisplayConfigurationService>.Instance);
            await native.InitializeAsync(null, markdownDisplayConfiguration: markdown);
            using var details = new DetailWindowController(native.Documents, native.Main,
                NullLogger<DetailWindowController>.Instance, NullLogger<NativeDetailRenderer>.Instance,
                NullLogger<StaticHtmlDetailRenderer>.Instance, markdown);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance, details);
            ResponseWindow? primary = null;
            var adapter = new DetailAnswerAdapter();
            var opened = new List<Kora.Core.Presentation.AdmittedDetailContent>();
            await using var composition = new Composition(f, async (content, live, resolve, token) =>
            {
                live().Should().BeTrue();
                if (route is "native-failure") { throw new InvalidOperationException("Synthetic unavailable native pane."); }
                var result = await details.OpenConversationDetailAsync(content, primary, live, resolve, token);
                opened.Add(content);
                return result;
            }, adapter: adapter);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            var preferences = new DetailPresentationSettingsTests.Preferences();
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), f.Time);
            settings.Observe();
            await using var offers = new DetailsOfferService(f.Store, f.Time);
            native.Main.BindVoiceOwnershipGate(static () => true);
            native.Main.BindDetailsOfferPriority(static () => true);
            composition.Audit.ExpectedOrigin = voiceOriginal ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
            native.Main.BindSessionCommands(composition.Workspace,
                new(composition.Workspace, composition.Conversation, composition.Access, f.Time));
            var owner = new SessionsPresentationOwner(composition.Model, native.Main);
            owner.BindDetailsOffers(offers, settings);
            var window = primary = responses.PrepareSessions(composition.Model, owner, static () => { }, static () => { });
            window.ShowSessions(expanded: true);
            await ActivateOriginalRequestWindow(window);
            try
            {
                await composition.Model.RefreshAsync();
                await composition.Model.SelectAsync(composition.Model.Sessions.Single());
                composition.Model.ComposerBody = "Independent unsent text";
                const string originalCommand = "ask this conversation with details offer Give the detailed plan";
                if (voiceOriginal)
                {
                    await native.Main.SetVoiceConsentAsync(true);
                    var reviewReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    native.Main.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName is nameof(native.Main.HasForegroundRequestReview) && native.Main.HasForegroundRequestReview)
                        { reviewReady.TrySetResult(); }
                    };
                    await native.Main.BeginPushToTalkCommand.ExecuteAsync();
                    await native.Main.EndPushToTalkCommand.ExecuteAsync();
                    native.Voice.PublishTranscript("Kora, " + originalCommand);
                    await reviewReady.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                }
                else
                {
                    native.Main.CommandText = originalCommand;
                    await native.Main.RunTypedCommand.ExecuteAsync();
                }
                native.Main.HasForegroundRequestReview.Should().BeTrue(native.Main.ResponseBody);
                native.Main.OfferDetailsOnce.Should().BeTrue();
                var panel = window.SessionView.FindControl<SessionRequestReviewPanel>("OriginalRequestReview")!;
                panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>()
                    .Single(button => button.Name is "ConfirmOriginalRequest")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                await Task.WhenAny(native.Main.DetailsOfferPresented, native.Main.DetailsOfferOperation)
                    .WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                native.Main.HasDetailsQuestion.Should().BeTrue(native.Main.ResponseBody + " / " + native.Main.Transcript);
                var original = composition.Model.ConversationMessages.Single();
                var terminal = await f.Store.ReadTaskAsync(original.Request.SessionId, original.Request.TaskId, f.Token);
                terminal!.Task.IsTerminal.Should().BeTrue();
                var question = native.Main.CurrentDetailsOffer!;
                question.Key.Request.TaskId.Should().NotBe(original.Request.TaskId);
                question.Key.Request.Origin.Should().Be(RequestOrigin.HostSystem);
                question.DetailsOffer!.OriginalRequest.Should().Be(original.Request);
                var retention = await f.Store.ReadRetentionAsync(original.Request.SessionId, f.Token);
                var immutable = await f.Store.ReadConversationMessageAsync(original.Request.SessionId, original.Request.RequestId, f.Token);
                var originalAudit = ReadDetailsOriginalAudit(f, original.Request.TaskId);
                using (var sealedSource = HostActivity.BeginRoot(original.Request, HostActivityLayer.Application, HostOperation.Presentation))
                {
                    var invalid = async () => await f.Questions.CreateAsync(original.Request,
                        Kora.Core.Presentation.DetailsOfferBinding.CreateSpec(), f.Time.Now.AddSeconds(60), f.Token);
                    await invalid.Should().ThrowAsync<InvalidDataException>("the original terminal-task gate must stay closed");
                }
                using (var exactControl = HostActivity.BeginRoot(question.Key.Request, HostActivityLayer.Application, HostOperation.Presentation))
                {
                    var arbitrary = async () => await f.Questions.CreateAsync(question.Key.Request,
                        new QuestionSpec("Unrelated question", QuestionKind.Text, [], maximumTextLength: 10),
                        f.Time.Now.AddSeconds(60), f.Token);
                    await arbitrary.Should().ThrowAsync<InvalidDataException>("the new control is not arbitrary question authority");
                }
                opened.Should().BeEmpty();
                original.Request.Origin.Should().Be(voiceOriginal ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
                var capturesBeforeAnswer = native.Voice.StartCalls;
                capturesBeforeAnswer.Should().Be(voiceOriginal ? 1 : 0, "question presentation cannot arm or open capture");
                var attemptYes = yes || route is "native-failure";
                var answer = panel.Children.OfType<StackPanel>().Last().Children.OfType<WrapPanel>()
                    .Single().Children.OfType<Button>().Single(button =>
                        string.Equals(button.Name, attemptYes ? "AcceptExactDetailsOffer" : "DeclineExactDetailsOffer", StringComparison.Ordinal));
                window.UpdateLayout();
                answer.IsEffectivelyVisible.Should().BeTrue();
                if (route is "native" or "native-failure")
                {
                    answer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await panel.Operation;
                }
                else if (route is "typed")
                {
                    native.Main.CommandText = yes ? "yes" : "not now";
                    await native.Main.RunTypedCommand.ExecuteAsync();
                }
                else if (route is "activated")
                {
                    await native.Main.SetVoiceConsentAsync(true);
                    await native.Main.BeginPushToTalkCommand.ExecuteAsync();
                    await native.Main.EndPushToTalkCommand.ExecuteAsync();
                    native.Voice.PublishTranscript(yes ? "Kora, yes" : "Kora, no");
                }
                else
                {
                    if (route is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
                    if (route is "hide") { window.Hide(); }
                    if (route is "composer") { composition.Model.ComposerBody = "Newer independent unsent text"; }
                    if (route is "unrelated-key") { window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3 }); }
                    if (route is "higher-priority") { native.Main.BindDetailsOfferPriority(static () => false); }
                    if (route is "preference") { settings.HoldUnavailable(); Dispatcher.UIThread.RunJobs(); }
                    if (route is "restart") { f.Reopen(); await f.Store.InitializeAsync(f.Token); }
                    if (route is "retention")
                    {
                        f.Time.Now = f.Time.Now.AddDays(1);
                        using var maintenance = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                            HostActivityLayer.Application, HostOperation.Retention);
                        await f.Store.ApplyRetentionAsync(static () => true, static (_, _) => ValueTask.CompletedTask, f.Token);
                    }
                    native.Main.RevalidateDetailsOffer();
                    if (route is "restart") { native.Main.RetireDetailsOffer(); }
                }
                await native.Main.DetailsOfferOperation.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                opened.Should().HaveCount(yes ? 1 : 0);
                window.SessionView.DetailsVisible.Should().Be(yes);
                if (yes)
                {
                    opened.Single().Source.Should().Be(DetailAnswerAdapter.Body);
                    var reader = window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
                    reader.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                    reader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(DetailAnswerAdapter.Body);
                    f.Time.Now = f.Time.Now.AddMinutes(2);
                    await details.RevalidateConversationDetailsAsync();
                    window.SessionView.DetailsVisible.Should().BeTrue("the accepted passive reader is independent of the retired offer deadline");
                    reader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(DetailAnswerAdapter.Body);
                }
                (await f.Store.ReadTaskAsync(original.Request.SessionId, original.Request.TaskId, f.Token))!.Task.Should().Be(terminal.Task);
                (await f.Store.ReadConversationMessageAsync(original.Request.SessionId, original.Request.RequestId, f.Token)).Should().Be(immutable);
                ReadDetailsOriginalAudit(f, original.Request.TaskId).Should().Equal(originalAudit);
                (await f.Store.ReadRetentionAsync(original.Request.SessionId, f.Token)).Should().Be(retention);
                composition.Model.ComposerBody.Should().Be(route is "composer" ? "Newer independent unsent text" : "Independent unsent text");
                adapter.Calls.Should().Be(1);
                preferences.Writes.Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                native.Voice.StartCalls.Should().Be(capturesBeforeAnswer + (route is "activated" ? 1 : 0));
                var finalQuestion = (await f.Store.ReadQuestionsAsync(original.Request.SessionId, f.Token)).Single();
                finalQuestion.Status.Should().NotBe(QuestionStatus.Pending);
                if (route is "native" or "typed" or "activated")
                { finalQuestion.AnswerChannel.Should().Be(route is "activated" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi); }
                var audit = WindowsSqliteConversationTests.AuditText(f);
                audit.Should().NotContain(original.Body).And.NotContain(DetailAnswerAdapter.Body);
                native.Main.CurrentDetailsOffer.Should().BeNull();
                var controlOutcome = await f.Store.ReadTaskAsync(original.Request.SessionId, question.Key.Request.TaskId, f.Token);
                controlOutcome!.Task.State.Should().Be(yes ? HostTaskState.Succeeded
                    : route is "native-failure" ? HostTaskState.Failed
                    : route is "restart" ? HostTaskState.Interrupted : HostTaskState.Cancelled);
            }
            finally { owner.Revoke(); }
        });
    }

    private static string[] ReadDetailsOriginalAudit(InteractionStorageFixture fixture, HostId<TaskIdentity> task)
    {
        using var connection = fixture.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events WHERE json_extract(envelope,'$.Request.TaskId.Value')=$task ORDER BY sequence;";
        command.Parameters.AddWithValue("$task", task.Value.ToString("D"));
        using var reader = command.ExecuteReader();
        var records = new List<string>();
        while (reader.Read()) { records.Add(reader.GetString(0)); }
        return records.ToArray();
    }
}