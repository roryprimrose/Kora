using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionDispositionReviewControllerTests
{
    [Fact]
    public async Task TypedCommandOpensOnlyExactNativeReviewAndClosingOwnerRetiresConfirmation()
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
            var metadata = await f.Interactions.ReadMetadataAsync(target.SessionId, f.Token);
            var tasks = await f.Interactions.ReadTaskPageAsync(target.SessionId, null, 50, f.Token);
            f.Main.CommandText = "review session disposition " + target.SessionId.Value.ToString("D") + " "
                + target.Generation.Value + " " + (metadata.Metadata?.Revision.Value ?? 0);
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(1);
            window!.IsVisible.Should().BeTrue();
            var model = (SessionsViewModel)window.DataContext!;
            model.SelectedSessionRecord.Should().Be(metadata);
            model.CanConfirmDisposition.Should().BeTrue();
            model.Detail.Should().Contain("not forensic deletion");
            f.Main.ResponseBody.Should().NotContain(metadata.Metadata?.Name.Value ?? "Unnamed")
                .And.NotContain(target.SessionId.Value.ToString("D"));
            controller.OpenCompact();
            window.Close();
            model.CanConfirmDisposition.Should().BeFalse("the original review owner is gone even while Compact still exists");
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(2);
            model.CanConfirmDisposition.Should().BeTrue();
            f.Main.CommandText = "confirm session disposition " + target.SessionId.Value.ToString("D") + " "
                + target.Generation.Value + " " + (metadata.Metadata?.Revision.Value ?? 0);
            await f.Main.RunTypedCommand.ExecuteAsync();
            f.Main.ResponseTitle.Should().Be("Session disposition review not accepted.");
            (await f.Interactions.ReadTaskPageAsync(target.SessionId, null, 50, f.Token)).Should().BeEquivalentTo(tasks);
            (await f.Interactions.ReadMetadataAsync(target.SessionId, f.Token)).Should().Be(metadata);
            window!.ClosePrivate();
            model.CanConfirmDisposition.Should().BeFalse();
            controller.Dispose();
            var review = () => controller.ReviewDispositionAsync(new(target.SessionId.Value, target.Generation.Value,
                metadata.Metadata?.Revision.Value ?? 0), RequestOrigin.LocalUi, () => true, f.Token);
            await review.Should().ThrowAsync<OperationCanceledException>();
        });
    }
}
