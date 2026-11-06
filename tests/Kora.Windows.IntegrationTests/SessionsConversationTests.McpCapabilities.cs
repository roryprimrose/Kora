using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task McpForegroundObservationIsSafeOffDispatcherAndTracksModalReturnAndRetirement()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await using var composition = new Composition(fixture);
            var presentation = new SessionsPresentationOwner(composition.Model);
            var owner = new SessionsWindow(composition.Model, presentation);
            owner.Show();
            try
            {
                HeadlessSession.SetActivation(owner, true);
                (await Task.Run(() => presentation.HasUniqueForegroundWindow, TestContext.Current.CancellationToken)).Should().BeTrue();
                HeadlessSession.SetActivation(owner, false);
                (await Task.Run(() => presentation.HasUniqueForegroundWindow, TestContext.Current.CancellationToken)).Should().BeFalse();
                HeadlessSession.SetActivation(owner, true);
                (await Task.Run(() => presentation.HasUniqueForegroundWindow, TestContext.Current.CancellationToken)).Should().BeTrue();
            }
            finally { owner.Close(); }
            (await Task.Run(() => presentation.HasUniqueForegroundWindow, TestContext.Current.CancellationToken)).Should().BeFalse();
        });
    }
}