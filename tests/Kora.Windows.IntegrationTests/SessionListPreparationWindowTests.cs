using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Core.Storage;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionListPreparationWindowTests
{
    [Theory]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("query")]
    [InlineData("source")]
    [InlineData("buffer")]
    [InlineData("name")]
    [InlineData("closure")]
    [InlineData("dispose")]
    public async Task QueuedExactPreparationCannotSubstituteANewerNativeSourceOrOwnershipEpoch(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            SessionsWindow? window = null;
            var opens = 0;
            using var observer = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => { window = view; opens++; });
            controller.Open();
            var model = (SessionsViewModel)window!.DataContext!;
            f.Main.CommandText = "prepare session list " + Guid.NewGuid().ToString("D");
            var preparation = f.Main.RunTypedCommand.ExecuteAsync();
            preparation.IsCompleted.Should().BeFalse("native preparation is queued to the dispatcher");
            switch (boundary)
            {
                case "owner": f.Access.SetOpen(false); f.Access.SetOpen(true); break;
                case "input": f.Main.CommandText = "different input"; break;
                case "query": model.ListQuery = "other"; model.ListQuery = string.Empty; break;
                case "source": model.RevokeSessionList(); break;
                case "buffer": model.ComposerBody = "new"; model.ComposerBody = string.Empty; break;
                case "name": model.NameDraft = "new"; model.NameDraft = string.Empty; break;
                case "closure": window.ClosePrivate(); break;
                default: controller.Dispose(); break;
            }
            await preparation;
            model.ListQuery.Should().BeEmpty();
            f.Main.ResponseTitle.Should().NotContain("PREPARED");
            if (f.Main.CanRevealPrivatePresentation) { f.Main.ResponseTitle.Should().Contain("unavailable"); }
            opens.Should().Be(1);
        });
    }

    [Fact]
    public async Task CommandPreparesAndFocusesTheSameExistingSingletonWithoutSelectionOrAutomaticSearch()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            SessionsWindow? window = null;
            var opened = 0;
            using var observer = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => { window = view; opened++; });
            controller.Open();
            var original = window!;
            var model = (SessionsViewModel)original.DataContext!;
            model.ListNameAvailability = SessionNameAvailability.Unnamed;
            model.ListMinimumNameLength = "not repaired";
            var id = Guid.NewGuid().ToString("D");
            f.Main.CommandText = "prepare session list " + id;
            await f.Main.RunTypedCommand.ExecuteAsync();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            opened.Should().Be(1);
            window.Should().BeSameAs(original);
            model.ListQuery.Should().Be(id);
            model.ListSearchKind.Should().Be(SessionListSearchKind.ExactId);
            model.ListNameAvailability.Should().Be(SessionNameAvailability.Unnamed);
            model.ListMinimumNameLength.Should().Be("not repaired");
            model.Sessions.Should().BeEmpty();
            model.SelectedSessionRecord.Should().BeNull();
            model.CanSearchList.Should().BeFalse();
            original.FindControl<TextBox>("ListQuery")!.IsFocused.Should().BeTrue();
            f.Main.ResponseTitle.Should().Contain("PREPARED");
            f.Main.CommandText = "session list help";
            await f.Main.RunTypedCommand.ExecuteAsync();
            model.ListQuery.Should().Be(id);
            opened.Should().Be(1);
            controller.Dispose();
            original.FocusPreparedListQuery().Should().BeFalse();
            f.Main.CommandText = "prepare session list " + id;
            await f.Main.RunTypedCommand.ExecuteAsync();
            f.Main.ResponseTitle.Should().Contain("unavailable");
            opened.Should().Be(1);
        });
    }
}
