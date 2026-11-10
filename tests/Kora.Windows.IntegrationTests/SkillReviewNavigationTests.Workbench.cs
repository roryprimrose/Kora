using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Skills;
using Kora.Definitions.Skills;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SkillReviewNavigationTests
{
    [Theory]
    [InlineData("hide-return")]
    [InlineData("minimize-restore")]
    [InlineData("leave-return")]
    public async Task HostedBundledInspectionRetiresExactSourceAndInvocationTargetOnPresentationLoss(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.InitializeAsync();
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            using var controller = Controller(f, f.SharedSkills);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.SkillPackages, view));
            var windows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SkillPackagesWindow>((_, _) => windows++);
            var catalogue = EmbeddedSkillCatalogue.Load();
            var id = catalogue.Packages[0].Manifest.Id;
            controller.OpenPackage(id, static () => true).Should().BeTrue();
            var window = responses.PrepareWorkbenchOwner();
            var view = controller.View!;
            var source = view.SelectedPackage!;
            controller.IsReviewing(source).Should().BeTrue();
            window.UpdateLayout();
            var readers = view.FindControl<TabControl>("SourceTabs")!.Items.OfType<TabItem>()
                .SelectMany(item => ((DockPanel)item.Content!).Children.OfType<TextBox>()).ToArray();
            readers.Should().HaveCount(source.Files.Count);
            for (var index = 0; index < readers.Length; index++) { readers[index].Text.Should().Be(source.Files[index].Text); }
            switch (boundary)
            {
                case "hide-return": window.Hide(); controller.Open(); break;
                case "minimize-restore": window.WindowState = WindowState.Minimized; window.WindowState = WindowState.Normal; break;
                case "leave-return": window.ShowSessions(expanded: true); controller.Open(); break;
            }
            Dispatcher.UIThread.RunJobs();
            readers.Should().OnlyContain(reader => string.IsNullOrEmpty(reader.Text));
            view.SelectedPackage.Should().BeNull();
            controller.IsReviewing(source).Should().BeFalse();
            view.FindControl<TabControl>("SourceTabs")!.ItemCount.Should().Be(0);
            windows.Should().Be(0);
            controller.OpenPackage(id, static () => true).Should().BeTrue();
            view.SelectedPackage.Should().NotBeNull();
            controller.IsReviewing(view.SelectedPackage!).Should().BeTrue();
            view.FindControl<Button>("CloseInspection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsVisible.Should().BeTrue();
            controller.View.Should().BeNull();
        });
    }

    [Fact]
    public async Task HostedBundledInspectorPreservesMetadataQueryWhenAlreadyPresentedAndNeverEnablesExecution()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.InitializeAsync();
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            using var controller = Controller(f, f.SharedSkills);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.SkillPackages, view));
            controller.Open();
            var view = controller.View!;
            view.FindControl<TextBox>("PackageQuery")!.Text = "unsubmitted metadata query";
            controller.Open();
            controller.View.Should().BeSameAs(view);
            view.FindControl<TextBox>("PackageQuery")!.Text.Should().Be("unsubmitted metadata query");
            view.FindControl<Button>("ReviewBundledRuntime")!.IsEnabled.Should().BeFalse();
            view.FindControl<Button>("ReviewBoundBundledExecution")!.IsEnabled.Should().BeFalse();
            view.FindControl<Button>("ReviewBundledExecution")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            responses.PrepareWorkbenchOwner().GetVisualDescendants().OfType<SkillPackagesView>().Should().ContainSingle();
            view.SelectedPackage.Should().BeNull();
        });
    }
}
