using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalEventBrokerWindowTests
{
    [WindowsFact]
    public async Task NativeCompletionNoticeControlsStayUnavailableWithoutAnEligibleVoiceOwnerAndPreserveFocus()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Storage.InitializeAsync();
            await using var broker = f.Broker();
            var model = f.Viewer(broker);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                var refresh = window.FindControl<Button>("ReadQueue")!;
                refresh.Focus().Should().BeTrue();
                window.FindControl<Button>("EnableCompletionNotices")!.IsEnabled.Should().BeFalse();
                window.FindControl<Button>("DisableCompletionNotices")!.IsEnabled.Should().BeFalse();
                model.CompletionNoticeStatus.Should().Contain("unavailable");
                await model.EnableCompletionNoticesAsync();
                model.Status.Should().Contain("unavailable/unconfirmed");
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(refresh);
            }
            finally { window.Close(); }
        });
    }
}