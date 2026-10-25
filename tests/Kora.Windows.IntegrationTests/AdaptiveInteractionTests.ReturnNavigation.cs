using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Conversation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task BackToConversationFocusesTheExactMessageWithoutClosingOrExecuting(int index)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            for (var message = 0; message < 2; message++)
            {
                f.Model.ComposerBody = "identical message titles and bodies";
                await f.Model.SubmitConversationAsync();
            }
            await f.Model.ReadConversationAsync();
            Dispatcher.UIThread.RunJobs();
            var original = f.Model.ConversationMessages[index];
            f.Model.ComposerBody = "keep independent unsent input";
            await f.Model.OpenConversationDetailAsync(original);
            f.Window!.UpdateLayout();
            var view = f.Window.SessionView;
            var reader = view.GetVisualDescendants().OfType<DetailContentView>().Single();
            var search = reader.FindControl<TextBox>("SearchInput")!;
            search.Text = "message";
            reader.FindControl<Button>("FirstMatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var calls = f.Admission.Calls;
            view.FindControl<TabControl>("ConversationTabs")!.SelectedIndex = 1;
            var back = view.FindControl<Button>("BackToConversation");
            back.Should().NotBeNull("the approved Details interaction includes an exact-origin return control");
            back!.IsEnabled.Should().BeTrue();
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            ConversationOrigin(view, original).IsFocused.Should().BeTrue();
            view.DetailsVisible.Should().BeTrue("returning changes focus, not the pane layout or content");
            view.GetVisualDescendants().OfType<DetailContentView>().Single().Should().BeSameAs(reader);
            search.Text.Should().Be("message");
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(original.Body);
            f.Model.ComposerBody.Should().Be("keep independent unsent input");
            f.Model.SelectedSessionRecord.Should().Be(f.Conversation);
            f.Model.ConversationMessages.Should().HaveCount(2);
            f.Admission.Calls.Should().Be(calls, "return navigation cannot rerun or submit a message");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HideReturnsToTheExactOriginAndRetiredReturnCannotNavigateAnotherSession(bool typed)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "original source";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            Dispatcher.UIThread.RunJobs();
            var original = f.Model.ConversationMessages.Single();
            await f.Model.OpenConversationDetailAsync(original);
            f.Window!.UpdateLayout();
            var view = f.Window.SessionView;
            var reader = view.GetVisualDescendants().OfType<DetailContentView>().Single();
            var back = view.FindControl<Button>("BackToConversation")!;
            if (typed) { await f.Run("hide details"); }
            else { view.FindControl<Button>("HideSessionDetails")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            ConversationOrigin(view, original).IsFocused.Should().BeTrue();
            view.DetailsVisible.Should().BeFalse();
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();

            await f.Model.OpenConversationDetailAsync(original);
            var next = f.Model.Sessions.First(row => row.Authority != f.Conversation.Authority);
            await f.Model.SelectAsync(next);
            f.Model.ComposerBody = "other conversation input";
            f.Window.UpdateLayout();
            var composer = view.FindControl<TextBox>("SessionComposer")!;
            composer.Focus().Should().BeTrue();
            back.IsEnabled.Should().BeFalse();
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            composer.IsFocused.Should().BeTrue("a delayed old return cannot choose a replacement origin");
            f.Model.SelectedSessionRecord.Should().Be(next);
            f.Model.ComposerBody.Should().Be("other conversation input");
            view.DetailsVisible.Should().BeFalse();
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Fact]
    public async Task ChooserHasNoMessageOriginUntilAnExactItemIsOpened()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "explicit choice required";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            Dispatcher.UIThread.RunJobs();
            var view = f.Window!.SessionView;
            await view.ShowAvailableDetailsAsync(f.Model);
            var back = view.FindControl<Button>("BackToConversation")!;
            back.IsEnabled.Should().BeFalse("a chooser is not an admitted message result");
            view.GetVisualDescendants().OfType<DetailContentView>().Should().BeEmpty();
            await f.Model.OpenConversationDetailAsync(f.Model.ConversationMessages.Single());
            back.IsEnabled.Should().BeTrue();
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Fact]
    public async Task EvidenceNeverInventsAConversationOrigin()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            await f.Run("show session logs");
            var view = f.Window!.SessionView;
            var back = view.FindControl<Button>("BackToConversation")!;
            back.IsEnabled.Should().BeFalse();
            view.FindControl<TextBlock>("DetailNavigationStatus")!.Text.Should().Contain("no message");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.DetailsVisible.Should().BeTrue();
            view.FindControl<Button>("HideSessionDetails")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FindControl<Button>("ShowSessionDetails")!.IsFocused.Should().BeTrue();
            f.Admission.Calls.Should().Be(0);
            f.Model.ConversationMessages.Should().BeEmpty();
        });
    }

    private static Button ConversationOrigin(SessionInteractionView view, ConversationMessage original) =>
        view.FindControl<TabControl>("ConversationTabs")!.GetVisualDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Name, "OpenConversationDetail", StringComparison.Ordinal)
                && Equals(button.DataContext, original));
}
