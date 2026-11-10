using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Skills;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SkillReviewNavigationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostedSharedSourceWithdrawalUsesSeparateSupportingConfirmationAndNeverReadsPackages(bool confirm)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.InitializeAsync();
            var preferences = new LocalSharedSkillPreferences(f);
            var source = new SharedSkillSource(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
            preferences.Save([source]);
            var reader = new NoSourceOperations();
            var discovery = new SharedSkillDiscoveryService(preferences, reader, f.SharedAdmission, f.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            using var controller = new SharedSkillSourcesWindowController(f.Main, discovery, static () => true,
                NullLogger<SharedSkillSourcesWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.SharedSkills, view));
            var windows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((_, _) => windows++);
            controller.TryOpen(static () => true).Should().BeTrue();
            await controller.Operation;
            var view = controller.View!;
            var shell = responses.PrepareWorkbenchOwner();
            view.RegisteredSources.Should().Equal(source);
            view.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
            view.FindControl<Button>("UnregisterSource")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var operation = controller.Operation;
            shell.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            preferences.Load().Should().Equal([source], "selection and review alone cannot withdraw read consent");
            var pane = shell.FindControl<ContentControl>("NativeReviewContent")!;
            var button = pane.GetVisualDescendants().OfType<Button>().Single(button => button.Content is string title
                && (confirm ? title.StartsWith("Unregister source:", StringComparison.Ordinal) : string.Equals(title, "Cancel", StringComparison.Ordinal)));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await operation;
            preferences.Load().Should().HaveCount(confirm ? 0 : 1);
            reader.Calls.Should().Be(0);
            windows.Should().Be(0);
            shell.IsVisible.Should().BeTrue();
            if (confirm) { view.Report("verified withdrawal"); view.RegisteredSources.Should().BeEmpty(); }
        });
    }

    [Theory]
    [InlineData("hide-return")]
    [InlineData("minimize-restore")]
    [InlineData("leave-return")]
    public async Task HostedSharedInspectionCancelsAndJoinsTheOriginalReadWithoutPublishingAfterReturn(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.InitializeAsync();
            var preferences = new LocalSharedSkillPreferences(f);
            var source = new SharedSkillSource(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
            preferences.Save([source]);
            var reader = new HeldSharedSourceReader(source);
            var discovery = new SharedSkillDiscoveryService(preferences, reader, f.SharedAdmission, f.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            using var controller = new SharedSkillSourcesWindowController(f.Main, discovery, static () => true,
                NullLogger<SharedSkillSourcesWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.SharedSkills, view));
            controller.Open();
            await controller.Operation;
            var shell = responses.PrepareWorkbenchOwner();
            var view = controller.View!;
            view.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
            view.FindControl<Button>("DiscoverSource")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var operation = controller.Operation;
            await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            try
            {
                switch (boundary)
                {
                    case "hide-return": shell.Hide(); controller.Open(); break;
                    case "minimize-restore": shell.WindowState = WindowState.Minimized; shell.WindowState = WindowState.Normal; break;
                    case "leave-return": shell.ShowSessions(expanded: true); controller.Open(); break;
                }
                reader.Token.IsCancellationRequested.Should().BeTrue();
                operation.IsCompleted.Should().BeFalse("cancellation does not complete the physical source read");
                view.RegisteredSources.Should().BeEmpty();
                view.FindControl<TextBox>("SharedSource")!.Text.Should().BeEmpty();
            }
            finally { reader.Release.TrySetResult(); }
            await operation;
            view.SelectedPackage.Should().BeNull();
            view.FindControl<TextBox>("SharedSource")!.Text.Should().BeEmpty();
            view.RegisteredSources.Should().BeEmpty();
            preferences.Load().Should().Equal(source);
        });
    }

    private sealed class HeldSharedSourceReader(SharedSkillSource source) : ISharedSkillSourceReader
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token { get; private set; }
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The read regression must not select or register another root.");
        public async ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource selected, CancellationToken cancellationToken)
        {
            selected.Should().Be(source);
            Token = cancellationToken;
            Entered.TrySetResult();
#pragma warning disable VSTHRD003 // Deliberately ignore cancellation until the exact held reader is released.
            await Release.Task;
#pragma warning restore VSTHRD003
            return new(source, [SharedSkillSnapshot.Parse(source.Id, "test\\SKILL.md",
                Encoding.UTF8.GetBytes("---\nname: Example\ndescription: Private test\n---\nPrivate original source"), false)]);
        }
    }
}
