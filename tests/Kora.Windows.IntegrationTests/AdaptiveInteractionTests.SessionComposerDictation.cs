using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionDictationPrimaryLauncherUsesComposedOwnerAndClosesOnExactSourceChange(bool rename)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var service = f.Native.Main.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Model.Dictation.Should().BeSameAs(service);
            f.Model.ComposerBody = "private unsent";
            var launcher = f.Window!.FindControl<SessionDictationButton>("SessionDictation")!;
            launcher.DataContext.Should().BeSameAs(f.Model);
            launcher.IsEnabled.Should().BeTrue();
            launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var window = launcher.DictationWindow!;
            window.IsVisible.Should().BeTrue();
            await window.BeginAsync();
            await window.EndAsync();
            service.IsActive.Should().BeFalse("the production-like unqualified capture must stay unavailable");
            service.Status.Should().Contain("unavailable");
            f.Model.ComposerBody.Should().Be("private unsent");
            if (rename)
            {
                var target = f.Model.SelectedSessionRecord!;
                await f.Native.Sessions.RenameAsync(target.Authority.SessionId, target.Authority.Generation,
                    target.Metadata!.Revision.Value, new("Renamed source"), RequestOrigin.LocalUi, f.Native.Token);
            }
            else { f.Window!.Hide(); }
            Dispatcher.UIThread.RunJobs();
            await window.CloseOperation;
            window.IsVisible.Should().BeFalse();
            f.Model.ComposerBody.Should().Be("private unsent");
        });
    }
}
