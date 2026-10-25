using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsSpecialistNavigationTests
{
    private static readonly string[] UnboundLinks =
        ["EditOwnedInstructions", "InspectExactGrants", "ReviewLocalVersion", "ReturnToVersionQuestion"];

    [Theory]
    [InlineData("EditOwnedInstructions")]
    [InlineData("InspectSkillPackages")]
    [InlineData("InspectExactGrants")]
    [InlineData("ReviewLocalVersion")]
    [InlineData("ReturnToVersionQuestion")]
    public Task SettingsLinksInvokeOnlyTheirBoundNativeDestination(string name) => HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
        await fixture.InitializeAsync();
        var opened = new List<string>();
        void Open(string destination)
        {
            HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.LocalUi);
            opened.Add(destination);
        }
        using var controller = new SettingsWindowController(fixture.Main, NullLogger<SettingsWindowController>.Instance)
        {
            EditOwnedInstructions = () => Open("EditOwnedInstructions"),
            InspectSkillPackages = () => Open("InspectSkillPackages"),
            InspectExactGrants = () => Open("InspectExactGrants"),
            ReviewLocalVersion = () => { Open("ReviewLocalVersion"); return Task.CompletedTask; },
            ReturnToVersionQuestion = () => { Open("ReturnToVersionQuestion"); return Task.CompletedTask; },
        };
        SettingsWindow? window = null;
        using var observe = Window.WindowOpenedEvent.AddClassHandler<SettingsWindow>((view, _) => window = view);
        fixture.Main.ShowSettings();
        opened.Should().BeEmpty();
        var button = window!.FindControl<Button>(name)!;
        button.IsEnabled.Should().BeTrue();
        await ((AsyncCommand)button.Command!).ExecuteAsync();
        opened.Should().Equal(name);
        fixture.Main.IsVoiceEnabled.Should().BeFalse();
        window!.Hide();
        Func<Task> hidden = () => ((AsyncCommand)button.Command!).ExecuteAsync();
        await hidden.Should().ThrowAsync<InvalidOperationException>();
        opened.Should().Equal(name);
        fixture.Main.ShowSettings();
        fixture.Main.CloseForObservedPrivacyEvent("Synthetic navigation privacy closure", hidePresentation: true);
        await fixture.Main.PrivacyClosureTask;
        Func<Task> privateNavigation = () => ((AsyncCommand)button.Command!).ExecuteAsync();
        await privateNavigation.Should().ThrowAsync<InvalidOperationException>();
        opened.Should().Equal(name);
    });

    [Fact]
    public Task UnboundLinksStayDisabledAndFailuresAreReported() => HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
        await fixture.InitializeAsync();
        using var controller = new SettingsWindowController(fixture.Main, NullLogger<SettingsWindowController>.Instance)
        {
            InspectSkillPackages = () => throw new InvalidOperationException("Synthetic navigation failure"),
        };
        SettingsWindow? window = null;
        using var observe = Window.WindowOpenedEvent.AddClassHandler<SettingsWindow>((view, _) => window = view);
        fixture.Main.ShowSettings();
        foreach (var name in UnboundLinks)
        {
            var button = window!.FindControl<Button>(name)!;
            button.IsEnabled.Should().BeFalse();
            button.Command.Should().BeNull();
        }
        window!.FindControl<Button>("InspectSkillPackages")!.Command!.Execute(null);
        fixture.Main.ResponseBody.Should().Contain("Settings navigation failed")
            .And.Contain("No operation completion is claimed").And.Contain("InvalidOperationException");
    });

    [Fact]
    public Task SessionsOpensCrossSessionAttachmentCatalogueWithoutSelectingASession() => HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
        await fixture.InitializeAsync();
        var opens = 0;
        using var controller = new SessionsWindowController(fixture.Main, fixture.Sessions, fixture.Evidence,
            fixture.Access, NullLogger<SessionsViewModel>.Instance)
        {
            OpenRetainedAttachments = () =>
            {
                HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.LocalUi);
                opens++;
            },
        };
        SessionsWindow? window = null;
        using var observe = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
        controller.Open();
        opens.Should().Be(0);
        var state = (SessionsViewModel)window!.DataContext!;
        state.SelectedSessionRecord.Should().BeNull();
        var button = window.FindControl<Button>("RetainedAttachments")!;
        button.IsVisible.Should().BeTrue();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        opens.Should().Be(1);
        state.SelectedSessionRecord.Should().BeNull();
        fixture.Access.SetOpen(false);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        opens.Should().Be(1);
        fixture.Main.ResponseBody.Should().Contain("Retained attachments navigation unavailable");
    });
}
