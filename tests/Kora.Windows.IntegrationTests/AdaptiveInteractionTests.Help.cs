using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Documentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Fact]
    public async Task Help_uses_the_same_shell_and_preserves_conversation_input()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            var window = f.Window!;
            f.Model.ComposerBody = "keep while reading help";
            using var help = new DocumentationWindowController(new Guide(), f.Native.Main,
                NullLogger<DocumentationWindowController>.Instance);
            help.BindWorkbench(() => window, view => window.ShowHostedView(WorkbenchActivity.Help, view));
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<DocumentationWindow>((_, _) => opens++);
            window.FindControl<Button>("HelpActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.ActiveActivity.Should().Be(WorkbenchActivity.Help);
            var view = (DocumentationView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            view.FindControl<StackPanel>("DocumentPanel")!.Children.Should().NotBeEmpty();
            opens.Should().Be(0);
            f.Model.ComposerBody.Should().Be("keep while reading help");
            window.FindControl<Button>("SessionsActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Sessions.InteractionOperation;
            Dispatcher.UIThread.RunJobs();
            f.Window.Should().BeSameAs(window);
            f.Model.ComposerBody.Should().Be("keep while reading help");
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task Closing_help_owner_cleans_up_without_recreating_a_shell()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            var window = f.Window!;
            using var help = new DocumentationWindowController(new Guide(), f.Native.Main,
                NullLogger<DocumentationWindowController>.Instance);
            var resolutions = 0;
            help.BindWorkbench(() => { resolutions++; return window; },
                view => window.ShowHostedView(WorkbenchActivity.Help, view));
            f.Native.Main.ShowDocumentation();
            var view = (DocumentationView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            window.Close();
            view.FindControl<StackPanel>("DocumentPanel")!.Children.Should().BeEmpty();
            resolutions.Should().Be(1);
            window.IsVisible.Should().BeFalse();
        });
    }

    private sealed class Guide : IUserDocumentationProvider
    {
        private readonly UserDocumentationPage page = new("start", "Start", "# Trusted help\nOnly embedded guidance.");
        public IReadOnlyList<UserDocumentationPage> GetPages() => [page];
        public UserDocumentationPage GetStartPage() => page;
    }
}
