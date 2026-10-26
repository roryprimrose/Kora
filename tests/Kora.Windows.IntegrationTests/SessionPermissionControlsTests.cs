using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Core.Authorization.SessionPermission;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionPermissionControlsTests
{
    [Fact]
    public async Task Native_controls_review_cancel_and_confirm_the_exact_session_without_selection_authority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
                new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var control = new SessionPermissionControls { DataContext = model };
            var window = new Window { Content = control };
            window.Show();
            try
            {
                ((Expander)control.Content!).IsExpanded = true;
                control.FindControl<Button>("ReadPolicy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await control.Operation;
                control.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 2;
                (await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token)).Level.Should().Be(SessionPermissionLevel.ReviewAll);
                control.FindControl<Button>("ReviewPolicy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.CanConfirmPermission.Should().BeTrue();
                control.FindControl<Button>("CancelPolicy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.CanConfirmPermission.Should().BeFalse();
                control.FindControl<Button>("ReviewPolicy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                control.FindControl<Button>("ConfirmPolicy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await control.Operation;
                (await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token)).Level.Should().Be(SessionPermissionLevel.ApproveAll);
                (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
            }
            finally { window.Close(); model.Close(); }
        });
    }
}
