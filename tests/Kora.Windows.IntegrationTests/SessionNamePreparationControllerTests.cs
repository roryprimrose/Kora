using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionNamePreparationControllerTests
{
    [Fact]
    public async Task ExactNamePreparationUsesExistingPrivateSessionsWithCompleteAccessibleReviewAndSeparateRename()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access, NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            SessionsWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => { window = view; opens++; });
            var target = f.LifecycleTarget!;
            var before = await f.Interactions.ReadMetadataAsync(target.SessionId, f.Token);
            var tasks = await f.Interactions.ReadTaskPageAsync(target.SessionId, null, 50, f.Token);
            f.Main.CommandText = "session name prepare " + target.SessionId.Value.ToString("D") + " \"Complete Café 東京 \"\"name\"\"\"";
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(1);
            window!.IsVisible.Should().BeTrue();
            var model = (SessionsViewModel)window.DataContext!;
            model.HasNamePreparation.Should().BeTrue();
            model.SelectedSessionRecord.Should().BeNull("a name is never selection authority");
            var review = window.FindControl<NamedTextBlock>("PreparedNameReview")!;
            review.Text.Should().Contain(before.Metadata?.Name.Value ?? "(unnamed; no metadata)")
                .And.Contain("Complete Café 東京 \"name\"").And.Contain(target.SessionId.Value.ToString("D"));
            review.Focusable.Should().BeTrue();
            AutomationProperties.GetName(review).Should().Contain("old and new literal");
            f.Main.Transcript.Should().NotContain("Café").And.NotContain(target.SessionId.Value.ToString("D"));
            f.Main.ResponseBody.Should().NotContain("Café");
            (await f.Interactions.ReadTaskPageAsync(target.SessionId, null, 50, f.Token)).Should().BeEquivalentTo(tasks);
            (await f.Interactions.ReadMetadataAsync(target.SessionId, f.Token)).Should().Be(before);
            window.FindControl<Button>("CancelPreparedName")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            model.HasNamePreparation.Should().BeFalse();
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(1);
            window.FindControl<Button>("RenamePreparedName")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.ConversationOperation;
            (await f.Interactions.ReadMetadataAsync(target.SessionId, f.Token)).Metadata!.Name.Value.Should().Be("Complete Café 東京 \"name\"");
            model.Status.Should().Contain("Committed explicit rename");
            await f.Main.RunTypedCommand.ExecuteAsync();
            controller.OpenCompact();
            window.Close();
            model.HasNamePreparation.Should().BeFalse("the exact review owner closed even while Compact exists");
            window.ClosePrivate();
            controller.Dispose();
            var unavailable = () => controller.PrepareNameAsync(
                Kora.Core.Commands.SessionNamePreparationCommand.Parse(f.Main.CommandText, "Kora")!,
                RequestOrigin.LocalUi, () => true, f.Token);
            await unavailable.Should().ThrowAsync<OperationCanceledException>();
        });
    }
}
