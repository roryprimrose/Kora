using AwesomeAssertions;

using Kora.Application.Tools;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
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
        f.Reasoner.Requests.Should().BeEmpty();
        var shutdown = 0;
        f.ViewModel.BindCustomizationDiscoveryShutdown(() => { shutdown++; return Task.CompletedTask; });
        await f.ViewModel.ExitAsync();
        shutdown.Should().Be(1);
        f.ViewModel.BindCustomizationDiscoveryShutdown(null);
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("system")]
    [InlineData("unactivated")]
    [InlineData("stale")]
    [InlineData("name")]
    [InlineData("pending")]
    public async Task DiscoveryCannotUseMissingRouteNonOriginalOrExpiredPrivateInput(string fault)
    {
        var f = fault is "name" or "pending" ? new Fixture() : await Fixture.CreateInitializedAsync();
        if (fault is "name")
        {
            f.NamePreferences.Name = "Supported Commands";
            await f.ViewModel.InitializeAsync();
        }
        if (fault is "pending")
        {
            f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
            f.Reasoner.Question = new("Original question?", ["one", "two"]);
            await f.ViewModel.InitializeAsync();
            await f.RunAsync("do the work");
            await f.ViewModel.ActiveReasoningTask!;
        }
        var calls = 0;
        if (fault is not "unbound")
        { f.ViewModel.CustomizationDiscoveryRequested += (_, _, _) => { calls++; return Task.CompletedTask; }; }
        if (fault is "system")
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
            await f.RunAsync("discover customizations");
        }
        else if (fault is "unactivated" or "name")
        { await f.RaiseActivatedTranscriptAsync(fault is "name" ? "Kora, discover customizations" : "discover customizations", 1); }
        else if (fault is "stale")
        {
            f.Voice.AfterStart = () => f.ViewModel.BindVoiceOwnershipGate(static () => false);
            await f.RaiseActivatedTranscriptAsync("Kora, discover customizations", 1);
        }
        else { await f.RunAsync("discover customizations"); }
        calls.Should().Be(0);
        if (fault is not "pending") { f.Reasoner.Requests.Should().BeEmpty(); }
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
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
