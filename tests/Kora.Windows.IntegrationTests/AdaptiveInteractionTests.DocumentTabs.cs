using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Application.Diagnostics;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Fact]
    public async Task ExactDocumentTabsDeduplicateReopenAndCloseWithoutRetainingInactiveBodies()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await PrepareDocumentMessages(f, 2);
            var messages = f.Model.ConversationMessages.ToArray();
            var view = f.Window!.SessionView;
            var tabs = view.FindControl<TabControl>("DocumentTabs")!;
            f.Model.ComposerBody = "independent unsent input";
            await f.Model.OpenConversationDetailAsync(messages[0]);
            f.Window.UpdateLayout();
            var original = DocumentReader(view);
            await f.Model.OpenConversationDetailAsync(messages[0]);
            tabs.ItemCount.Should().Be(1);
            DocumentReader(view).Should().BeSameAs(original);
            await f.Model.OpenConversationDetailAsync(messages[1]);
            f.Window.UpdateLayout();
            var replacement = DocumentReader(view);
            original.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            tabs.ItemCount.Should().Be(2);
            tabs.SelectedItem = tabs.Items[0];
            await view.Operation;
            f.Window.UpdateLayout();
            DocumentReader(view).FindControl<TextBox>("ContentReader")!.Text.Should().Be(messages[0].Body);
            replacement.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            f.Model.ComposerBody.Should().Be("independent unsent input");
            f.Admission.Calls.Should().Be(2);

            var first = (TabItem)tabs.Items[0]!;
            var close = ((StackPanel)first.Header!).Children.OfType<Button>()
                .Single(button => string.Equals(button.Name, "CloseDocumentTab", StringComparison.Ordinal));
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tabs.ItemCount.Should().Be(1);
            view.DetailsVisible.Should().BeFalse();
            view.GetVisualDescendants().OfType<DetailContentView>().Should().BeEmpty();
            f.Model.ComposerBody.Should().Be("independent unsent input");
        });
    }

    [Fact]
    public async Task ReturningToASessionShowsOnlyItsMetadataUntilAnExactTabIsSelected()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await PrepareDocumentMessages(f, 1);
            var message = f.Model.ConversationMessages.Single();
            await f.Model.OpenConversationDetailAsync(message);
            var view = f.Window!.SessionView;
            var tabs = view.FindControl<TabControl>("DocumentTabs")!;
            f.Window.UpdateLayout();
            var old = DocumentReader(view);
            await f.Model.SelectAsync(f.Model.Sessions.First(row => row.Authority != f.Conversation.Authority));
            tabs.ItemCount.Should().Be(0);
            old.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            await f.SelectConversation();
            tabs.ItemCount.Should().Be(1);
            view.DetailsVisible.Should().BeFalse();
            view.GetVisualDescendants().OfType<DetailContentView>().Should().BeEmpty();
            await f.Model.ReadConversationAsync();
            await view.ShowAvailableDetailsAsync(f.Model, choose: true);
            f.Window.UpdateLayout();
            var tab = (TabItem)tabs.Items[0]!;
            ((StackPanel)tab.Header!).Children.OfType<Button>()
                .Single(button => string.Equals(button.Name, "OpenDocumentTab", StringComparison.Ordinal))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.Operation;
            f.Window.UpdateLayout();
            DocumentReader(view).FindControl<TextBox>("ContentReader")!.Text.Should().Be(message.Body);
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Fact]
    public async Task NinthDocumentTabIsRefusedWithoutEvictingAnExactReference()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await PrepareDocumentMessages(f, 9);
            var messages = f.Model.ConversationMessages.ToArray();
            var view = f.Window!.SessionView;
            var tabs = view.FindControl<TabControl>("DocumentTabs")!;
            foreach (var message in messages.Take(8)) { await f.Model.OpenConversationDetailAsync(message); }
            tabs.ItemCount.Should().Be(8);
            var references = tabs.Items.OfType<TabItem>().Select(item => item.Tag).ToArray();
            await f.Model.OpenConversationDetailAsync(messages[8]);
            tabs.Items.OfType<TabItem>().Select(item => item.Tag).Should().Equal(references);
            f.Model.ConversationStatus.Should().Contain("unavailable");
            f.Admission.Calls.Should().Be(9);
        });
    }

    [Fact]
    public async Task PrivacyClosureRetiresTabLabelsReferencesAndBodies()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await PrepareDocumentMessages(f, 1);
            await f.Model.OpenConversationDetailAsync(f.Model.ConversationMessages.Single());
            f.Window!.UpdateLayout();
            var view = f.Window.SessionView;
            var reader = DocumentReader(view);
            f.Native.Main.CloseForObservedPrivacyEvent("Synthetic document tab retirement", hidePresentation: true);
            await f.Native.Main.PrivacyClosureTask;
            view.FindControl<TabControl>("DocumentTabs")!.ItemCount.Should().Be(0);
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            view.DetailsVisible.Should().BeFalse();
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Fact]
    public async Task ActivityAndEvidenceTabsExplicitlyReopenTheirOriginalObservedSources()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await PrepareDocumentMessages(f, 1);
            await f.Model.ReadHistoryAsync();
            f.Model.SelectHistoryRecord(f.Model.HistoryRecords[0]);
            await f.Model.OpenHistoryDetailAsync();
            f.Window!.UpdateLayout();
            var view = f.Window.SessionView;
            var tabs = view.FindControl<TabControl>("DocumentTabs")!;
            var receipt = DocumentReader(view).FindControl<TextBox>("ContentReader")!.Text;
            await f.Model.ReadEvidenceDetailsAsync(EvidenceSource.AuthorityAudit);
            var snapshot = f.Model.EvidenceDetails.Single();
            var evidence = EvidenceText(view);
            tabs.ItemCount.Should().Be(2);
            tabs.SelectedItem = tabs.Items[0];
            await view.Operation;
            f.Window.UpdateLayout();
            DocumentReader(view).FindControl<TextBox>("ContentReader")!.Text.Should().Be(receipt);
            evidence.Text.Should().BeNull();
            tabs.SelectedItem = tabs.Items[1];
            await view.Operation;
            view.DetailsVisible.Should().BeTrue(f.Model.Status);
            f.Model.EvidenceDetails.Should().ContainSingle().Which.Should().BeSameAs(snapshot);
            EvidenceText(view).Text.Should().NotBeNull();
            f.Admission.Calls.Should().Be(1);
        });
    }

    private static DetailContentView DocumentReader(SessionInteractionView view) =>
        view.GetVisualDescendants().OfType<DetailContentView>().Single();

    [Fact]
    public async Task EmbeddedGuideOpensAndReopensAsAnExactTabWithoutAnotherWindowOrConversationRead()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            var window = f.Window!;
            var page = f.Native.Documents.GetStartPage();
            var extraWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<DetailWindow>((_, _) => extraWindows++);
            f.Details.OpenEmbeddedPage(page, window);
            window.UpdateLayout();
            var view = window.SessionView;
            var tabs = view.FindControl<TabControl>("DocumentTabs")!;
            tabs.ItemCount.Should().Be(1);
            var original = DocumentReader(view);
            original.FindControl<TextBox>("ContentReader")!.Text.Should().NotBeNullOrEmpty();
            view.HideDetailsAndRestoreFocus();
            original.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            f.Model.CanShowDetails.Should().BeTrue("the remembered guide descriptor is available without reading a conversation");
            await view.ShowAvailableDetailsAsync(f.Model, choose: true);
            window.UpdateLayout();
            OpenTabHeader(tabs, 0);
            await view.Operation;
            window.UpdateLayout();
            DocumentReader(view).Should().NotBeSameAs(original);
            view.FindControl<TextBlock>("DetailsHeading")!.Text.Should().Be("Embedded Kora guidance");
            f.Model.SelectedSessionRecord.Should().BeNull();
            f.Model.ConversationMessages.Should().BeEmpty();
            f.Admission.Calls.Should().Be(0);
            extraWindows.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentResponseTabReopensOnlyItsExactStillCurrentResponse(bool replace)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await PrepareDocumentMessages(f, 1);
            var message = f.Model.ConversationMessages.Single();
            await f.Run("what version are you running");
            await f.Native.Main.OpenCurrentResponseDetailsAsync();
            f.Window!.UpdateLayout();
            var view = f.Window.SessionView;
            var tabs = view.FindControl<TabControl>("DocumentTabs")!;
            var original = DocumentReader(view).FindControl<TextBox>("ContentReader")!.Text;
            await f.Model.OpenConversationDetailAsync(message);
            if (replace) { await f.Run("what power action is pending"); }
            OpenTabHeader(tabs, 0);
            await view.Operation;
            f.Window.UpdateLayout();
            if (replace)
            {
                view.DetailsVisible.Should().BeFalse();
                view.GetVisualDescendants().OfType<DetailContentView>().Should().BeEmpty();
                f.Native.Main.Transcript.Should().Contain("Current response action unavailable");
            }
            else
            {
                DocumentReader(view).FindControl<TextBox>("ContentReader")!.Text.Should().Be(original);
                view.FindControl<TextBlock>("DetailsHeading")!.Text.Should().Contain("not the selected conversation");
            }
            f.Model.SelectedSessionRecord!.Authority.Should().Be(f.Conversation.Authority);
            f.Admission.Calls.Should().Be(1);
        });
    }

    private static void OpenTabHeader(TabControl tabs, int index)
    {
        var tab = (TabItem)tabs.Items[index]!;
        ((StackPanel)tab.Header!).Children.OfType<Button>()
            .Single(button => string.Equals(button.Name, "OpenDocumentTab", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Theory]
    [InlineData("hide-return")]
    [InlineData("minimize-restore")]
    [InlineData("collapse-return")]
    [InlineData("close-tab")]
    [InlineData("select-tab")]
    public async Task LateTabReadCannotPublishAfterItsOriginalPresentationIsRetired(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var reader = new RequestAuditReader((ICommittedAuthorityAuditReader)f.Native.Interactions);
            await f.Initialize(new(reader, f.Native.Access, f.Native.Time, NullLogger<DurableEvidenceQuery>.Instance));
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "exact tab read source";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            await f.Model.ReadEvidenceDetailsAsync(EvidenceSource.AuthorityAudit);
            var view = f.Window!.SessionView;
            var original = EvidenceText(view);
            await f.Model.OpenConversationDetailAsync(f.Model.ConversationMessages.Single());
            var tabs = view.FindControl<TabControl>("DocumentTabs")!;
            reader.Hold = true;
            tabs.SelectedItem = tabs.Items[0];
            var operation = view.Operation;
            await reader.Entered.Task;
            try
            {
                switch (boundary)
                {
                    case "hide-return":
                        f.Window.Hide();
                        await f.Run("show sessions");
                        break;
                    case "minimize-restore":
                        f.Window.WindowState = WindowState.Minimized;
                        f.Window.WindowState = WindowState.Normal;
                        break;
                    case "collapse-return":
                        await f.Run("collapse conversation");
                        await f.Run("show sessions");
                        break;
                    case "close-tab":
                        var item = (TabItem)tabs.Items[0]!;
                        ((StackPanel)item.Header!).Children.OfType<Button>()
                            .Single(button => string.Equals(button.Name, "CloseDocumentTab", StringComparison.Ordinal))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "select-tab":
                        tabs.SelectedItem = tabs.Items[1];
                        break;
                }
                operation.IsCompleted.Should().BeFalse("retirement is not physical read completion");
                original.Text.Should().BeNull();
            }
            finally { reader.Release.TrySetResult(); }
            await operation;
            original.Text.Should().BeNull();
            view.DetailsVisible.Should().BeFalse(f.Model.Status);
            view.GetVisualDescendants().OfType<DetailContentView>().Should().BeEmpty();
            f.Admission.Calls.Should().Be(1);
        });
    }

    private static async Task PrepareDocumentMessages(Fixture f, int count)
    {
        await f.Initialize();
        await f.Run("show sessions");
        await f.SelectConversation();
        f.Admission.AllowUnavailable = true;
        for (var index = 0; index < count; index++)
        {
            f.Model.ComposerBody = $"Exact document source {index}";
            await f.Model.SubmitConversationAsync();
        }
        await f.Model.ReadConversationAsync();
        Dispatcher.UIThread.RunJobs();
    }
}
