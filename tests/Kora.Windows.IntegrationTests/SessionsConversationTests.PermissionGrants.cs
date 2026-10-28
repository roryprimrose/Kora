using Avalonia.Controls;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionPermissionNavigationCapturesOnlyTheOriginalSelectedSessionAndRetiresOnSwitch(bool primary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new Composition(f);
            var model = composition.Model;
            Window window = primary
                ? new Window { Content = new SessionInteractionView { DataContext = model }, DataContext = model }
                : new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == first));
                HostId<SessionIdentity>? opened = null;
                Func<bool>? admitted = null;
                model.InspectPermissionGrants = (target, eligibility) => { opened = target; admitted = eligibility; };
                var button = window.GetLogicalDescendants().OfType<Button>()
                    .Single(item => ReferenceEquals(item.Command, model.PermissionGrantsCommand));
                button.IsEnabled.Should().BeTrue();
                await ((AsyncCommand)button.Command!).ExecuteAsync();
                opened.Should().Be(first);
                admitted.Should().NotBeNull();
                admitted!().Should().BeTrue();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != first));
                admitted().Should().BeFalse();
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
            }
            finally { window.Close(); }
        });
    }
}
