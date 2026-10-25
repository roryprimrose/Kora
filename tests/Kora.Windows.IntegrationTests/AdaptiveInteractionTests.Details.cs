using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Commands;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    private static readonly string[] ExactDetailBodies = ["first exact result source", "second exact result source"];
    private static readonly string[] EvidenceDetailCommands = ["show session logs", "show session audit"];

    [Fact]
    public async Task ExactMessageLinksShareOneResizablePaneAndHidePreservesTheComposer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            var view = f.Window!.SessionView;
            var show = view.FindControl<Button>("ShowSessionDetails")!;
            show.IsEnabled.Should().BeFalse("metadata selection cannot claim unread message details");
            f.Admission.AllowUnavailable = true;
            foreach (var body in ExactDetailBodies)
            {
                f.Model.ComposerBody = body;
                await f.Model.SubmitConversationAsync();
            }
            await f.Model.ReadConversationAsync();
            Dispatcher.UIThread.RunJobs();
            f.Model.ConversationMessages.Should().HaveCount(2, f.Model.ConversationStatus);
            show.IsEnabled.Should().BeTrue(f.Model.DetailsAvailability);
            f.Model.ComposerBody = "keep this unsent";
            var messages = f.Model.ConversationMessages.ToArray();
            var detailWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<DetailWindow>((_, _) => detailWindows++);
            await f.Model.OpenConversationDetailAsync(messages[0]);
            f.Window.IsExpanded.Should().BeTrue();
            view.DetailsVisible.Should().BeTrue();
            f.Window.MinWidth.Should().Be(1040);
            f.Window.UpdateLayout();
            var firstReader = view.GetVisualDescendants().OfType<DetailContentView>().Single();
            firstReader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(messages[0].Body);
            view.FindControl<TextBox>("SessionComposer")!.IsEffectivelyVisible.Should().BeTrue();
            view.FindControl<GridSplitter>("DetailsDivider")!.IsVisible.Should().BeTrue();

            await f.Model.OpenConversationDetailAsync(messages[1]);
            f.Window.UpdateLayout();
            firstReader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            var secondReader = view.GetVisualDescendants().OfType<DetailContentView>().Single();
            secondReader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(messages[1].Body);
            detailWindows.Should().Be(0, "session details must not create another native window");
            f.Model.ComposerBody.Should().Be("keep this unsent");
            var sourceToggle = secondReader.FindControl<CheckBox>("SourceToggle")!;
            sourceToggle.IsChecked = true;
            var readerText = secondReader.FindControl<TextBox>("ContentReader")!;
            readerText.CaretIndex = 5;
            var split = view.FindControl<Grid>("ReadingPanes")!;
            split.ColumnDefinitions[0].Width = new(2, GridUnitType.Star);
            split.ColumnDefinitions[2].Width = new(1, GridUnitType.Star);
            view.FindControl<Button>("HideSessionDetails")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            secondReader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            view.DetailsVisible.Should().BeFalse();
            f.Model.CanShowDetails.Should().BeTrue();
            f.Model.ComposerBody.Should().Be("keep this unsent");

            await f.Run("collapse conversation");
            await f.Run("show me the detailed results");
            f.Window.UpdateLayout();
            f.Window.IsExpanded.Should().BeTrue();
            view.DetailsVisible.Should().BeTrue();
            view.GetVisualDescendants().OfType<DetailContentView>().Single()
                .FindControl<TextBox>("ContentReader")!.Text.Should().Be(messages[1].Body);
            var restored = view.GetVisualDescendants().OfType<DetailContentView>().Single();
            restored.FindControl<CheckBox>("SourceToggle")!.IsChecked.Should().BeTrue();
            restored.FindControl<TextBox>("ContentReader")!.CaretIndex.Should().Be(5);
            split.ColumnDefinitions[0].Width.Value.Should().Be(2);
            f.Model.ComposerBody.Should().Be("keep this unsent");
        });
    }

    [Fact]
    public async Task SessionSwitchAndPrivacyRevokeContentWithoutRetargetingThePreviousResult()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "private original";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            Dispatcher.UIThread.RunJobs();
            f.Model.ConversationMessages.Should().ContainSingle(f.Model.ConversationStatus);
            await f.Model.OpenConversationDetailAsync(f.Model.ConversationMessages.Single());
            f.Window!.SessionView.DetailsVisible.Should().BeTrue(f.Model.ConversationStatus);
            f.Window.UpdateLayout();
            var reader = f.Window!.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
            await f.Run("select the last session");
            f.Window.SessionView.DetailsVisible.Should().BeFalse();
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            f.Model.CanShowDetails.Should().BeFalse();
            f.Native.Main.CloseForObservedPrivacyEvent("Synthetic Details retirement", hidePresentation: true);
            await f.Native.Main.PrivacyClosureTask;
            f.Window.IsVisible.Should().BeFalse();
            f.Model.ConversationMessages.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task SessionLogAndAuditCommandsReuseThePaneAndEvidenceLinkWithoutModelDispatch()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            foreach (var command in EvidenceDetailCommands)
            {
                await f.Run(command);
                f.Model.HasEvidenceDetails.Should().BeTrue("{0}: {1} / {2}", command, f.Model.Status, f.Native.Main.ResponseBody);
                f.Window!.SessionView.DetailsVisible.Should().BeTrue();
                f.Window.UpdateLayout();
                var body = f.Window.SessionView.GetVisualDescendants().OfType<NamedTextBlock>()
                    .Single(text => text.Text?.Contains("\"Disclosure\"", StringComparison.Ordinal) == true);
                body.Text.Should().Contain("Read-only");
                await f.Run("hide details");
                body.Text.Should().BeNull("hiding releases the displayed evidence text");
                f.Window.SessionView.DetailsVisible.Should().BeFalse();
                await f.Model.OpenEvidenceDetailsAsync();
                f.Window.SessionView.DetailsVisible.Should().BeTrue();
            }
            f.Model.EvidenceDetails.Should().HaveCount(2);
            var first = f.Model.EvidenceDetails[0];
            await f.Model.OpenEvidenceDetailsAsync(first);
            f.Model.LastEvidenceDetail.Should().BeSameAs(first);
            await f.Window!.SessionView.RevalidateEvidenceAsync();
            f.Window.SessionView.DetailsVisible.Should().BeTrue();
            f.Admission.Calls.Should().Be(0);
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public async Task CurrentResponseDetailsNameTheirOwnSourceAndDoNotRetargetTheSelectedComposer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            var selected = f.Model.SelectedSessionRecord;
            f.Model.ComposerBody = "do not submit or move";
            await f.Run("what version are you running");
            await f.Native.Main.OpenCurrentResponseDetailsAsync();
            f.Window!.SessionView.DetailsVisible.Should().BeTrue(f.Native.Main.Transcript);
            f.Window.SessionView.FindControl<TextBlock>("DetailsHeading")!.Text.Should().Contain("not the selected conversation");
            f.Model.SelectedSessionRecord.Should().BeSameAs(selected);
            f.Model.ComposerBody.Should().Be("do not submit or move");
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task LateEvidenceValidationCannotClearAReplacementAndSourceLossClearsTheExactDisplayedBuffer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            var view = f.Window!.SessionView;
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken retired = default;
            var first = new SessionEvidenceDetail(EvidenceSource.Log,
                new(EvidencePageStatus.Available, [], null, [], "first private evidence"));
            view.ShowEvidence(first, () => true, token =>
            { retired = token; return pending.Task; }).Should().BeTrue();
            var firstBody = EvidenceText(view);
            var validation = view.RevalidateEvidenceAsync();
            view.HideDetails();
            retired.IsCancellationRequested.Should().BeTrue();
            var second = new SessionEvidenceDetail(EvidenceSource.AuthorityAudit,
                new(EvidencePageStatus.Available, [], null, [], "second private evidence"));
            view.ShowEvidence(second, () => true, _ => Task.FromResult(false)).Should().BeTrue();
            var secondBody = EvidenceText(view);
            firstBody.Text.Should().BeNull();
            pending.SetResult(false);
            await validation;
            view.DetailsVisible.Should().BeTrue();
            secondBody.Text.Should().Contain("second private evidence").And.NotContain("first private evidence");
            await view.RevalidateEvidenceAsync();
            view.DetailsVisible.Should().BeFalse();
            secondBody.Text.Should().BeNull();
            f.Admission.Calls.Should().Be(0);
        });
    }

    private static NamedTextBlock EvidenceText(SessionInteractionView view) =>
        view.FindControl<ContentControl>("DetailsContent")!.Content.Should().BeOfType<ScrollViewer>()
            .Which.Content.Should().BeOfType<NamedTextBlock>().Which;

    [Fact]
    public async Task ActivatedVoiceCannotBecomeLocalUiEvidenceAdmission()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
                HostActivityLayer.Desktop, HostOperation.Request);
            await f.Sessions.ShowDetailsAsync(WorkspaceSurfaceTarget.SessionAudit, () => true);
            f.Native.Main.ResponseBody.Should().Contain("requires an original local UI request");
            f.Model.EvidenceDetails.Should().BeEmpty();
            f.Window!.SessionView.DetailsVisible.Should().BeFalse();
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task ExactEvidenceRevalidationDoesNotRetargetNewRowsAndSourceRemovalRetiresThePane()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            var subject = f.Model.SelectedSessionRecord!.Authority.SessionId;
            var sink = new WindowsSqliteEvidenceSink(f.Native);
            sink.Initialize();
            var id = Guid.NewGuid();
            void Seed(Guid identity, string text)
            {
                using var producer = HostActivity.BeginRoot(
                    new(new(Guid.NewGuid()), subject, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                    HostActivityLayer.Desktop, HostOperation.Request);
                sink.WriteDiagnostic(new(1, new(identity), DateTimeOffset.UtcNow, 12345, "Observed", "Information",
                    "session.details.test", text, new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [],
                    TraceSnapshot.Capture(producer.Activity), producer.Request, null, producer.CorrelationId));
            }
            Seed(id, "first exact source record");
            await f.Run("show session logs");
            var snapshot = f.Model.EvidenceDetails.Single();
            snapshot.Page.Records.Should().ContainSingle(record => record.Reference.Id.Value == id);
            var view = f.Window!.SessionView;
            var body = EvidenceText(view);
            Seed(Guid.NewGuid(), "later result must not replace the first");
            await view.RevalidateEvidenceAsync();
            view.DetailsVisible.Should().BeTrue();
            body.Text.Should().Contain("first exact source record").And.NotContain("later result");
            sink.PruneOrdinaryDiagnostics(DateTimeOffset.MaxValue, f.Native.Token).Logs.Should().BeGreaterThan(0);
            await view.RevalidateEvidenceAsync();
            view.DetailsVisible.Should().BeFalse();
            body.Text.Should().BeNull();
            f.Model.EvidenceDetails.Should().BeEmpty();
            f.Model.Status.Should().Contain("retired");
            f.Admission.Calls.Should().Be(0);
        });
    }
}
