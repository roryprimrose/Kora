using AwesomeAssertions;

using Kora.Application.Tools;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task MissingDiscoverySurfaceRemainsExplicitlyUnavailableWithoutFallback()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.CallState.SetState(Kora.Core.Communication.CallState.Clear);
        await f.Dispatcher.LastInvocation;
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        var tasks = f.HostStore.Records.Count;
        await f.RunAsync("discover customizations Which can help?");
        f.ViewModel.ResponseBody.Should().Contain("metadata discovery surface is unavailable");
        f.HostStore.Records.Count.Should().Be(tasks);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoveryRoutesOriginalTypedAndActivatedInputWithoutLegacyReasoning(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var calls = 0;
        f.ViewModel.CustomizationDiscoveryRequested += (command, origin, current) =>
        {
            command.Request.Should().Be("Which can help?");
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            current().Should().BeTrue();
            calls++;
            return Task.CompletedTask;
        };
        if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, discover customizations Which can help?", 1); }
        else { await f.RunAsync("discover customizations Which can help?"); }
        calls.Should().Be(1);

        var shutdown = 0;
        f.ViewModel.BindCustomizationDiscoveryShutdown(() => { shutdown++; return Task.CompletedTask; });
        await f.ViewModel.ExitAsync();
        shutdown.Should().Be(1);
        f.ViewModel.BindCustomizationDiscoveryShutdown(null);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("show skills", null)]
    [InlineData("discover customizations", "")]
    [InlineData("Kora, discover customizations", "")]
    [InlineData("discover customizations  original question  ", " original question  ")]
    public void DiscoveryCommandIsExactAndPreservesQuestionWhitespace(string text, string? request) =>
        CustomizationDiscoveryCommand.Parse(text, "Kora")?.Request.Should().Be(request);

    [Fact]
    public async Task ExplicitTypedDiscoveryDoesNotInventAnUnavailableActivationName()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.CustomizationDiscoveryRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync("discover customizations");
        calls.Should().Be(1);
        await f.RunAsync("Kora, discover customizations");
        calls.Should().Be(1);

    }
}
