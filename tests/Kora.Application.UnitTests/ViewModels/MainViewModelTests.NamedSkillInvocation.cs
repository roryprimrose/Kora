using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedSkillNamespacePreservesOriginalChannelAndNeverFallsBackToStaticOrModelRouting(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var calls = new List<NamedSkillCommand>();
        f.ViewModel.BindNamedSkillChoiceCapture(() => static () => true);
        f.ViewModel.NamedSkillInvocationRequested += (command, origin, eligible) =>
        {
            eligible().Should().BeTrue();
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            calls.Add(command);
            return Task.CompletedTask;
        };
        foreach (var text in new[] { "use skill Lock", "skill choice 1", "skill close" })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        calls.Select(call => call.Action).Should().Equal("Review", "Choice", "Close");
        calls[0].Name.Should().Be("Lock");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("use skill")]
    [InlineData("use skill \u202eLock")]
    [InlineData("skill choice 0")]
    [InlineData("skill anything")]
    public async Task MalformedNamedRequestsClarifyWithoutRunningTheirText(string text)
    {
        var f = await Fixture.CreateInitializedAsync();
        var called = false;
        f.ViewModel.NamedSkillInvocationRequested += (_, _, _) => { called = true; return Task.CompletedTask; };
        await f.RunAsync(text);
        called.Should().BeFalse();
        f.ViewModel.Transcript.Should().Contain("Nothing was run");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NamedSkillUnavailableBindingOrNonOriginalInputNeverBecomesFallbackAuthority()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("use skill Lock");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        var called = false;
        f.ViewModel.NamedSkillInvocationRequested += (_, _, _) => { called = true; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync("use skill Lock", 1);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        { await f.RunAsync("use skill Lock"); }
        called.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NamedSkillCannotReplaceAPendingQuestionAndExitJoinsItsOwnedWork()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        f.Reasoner.Question = new("Keep this exact question?", ["one", "two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("original question task");
        await f.ViewModel.ActiveReasoningTask!;
        var called = false;
        f.ViewModel.NamedSkillInvocationRequested += (_, _, _) => { called = true; return Task.CompletedTask; };
        await f.RunAsync("use skill Lock");
        called.Should().BeFalse();
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        var joined = 0;
        f.ViewModel.BindNamedSkillUseShutdown(() => { joined++; return Task.CompletedTask; });
        await f.ViewModel.ExitAsync();
        joined.Should().Be(1);
    }

    [Fact]
    public async Task NamedSkillUnavailableAssistantPrefixDoesNotCreateAuthority()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var called = false;
        f.ViewModel.NamedSkillInvocationRequested += (_, _, _) => { called = true; return Task.CompletedTask; };
        await f.RunAsync("Kora, use skill Lock");
        called.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedSkillChoiceWithoutItsExactCapturedHostSetCannotRetarget(bool bound)
    {
        var f = await Fixture.CreateInitializedAsync();
        if (bound) { f.ViewModel.BindNamedSkillChoiceCapture(() => static () => false); }
        var called = false;
        f.ViewModel.NamedSkillInvocationRequested += (_, _, _) => { called = true; return Task.CompletedTask; };
        await f.RunAsync("skill choice 1");
        called.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.BindNamedSkillChoiceCapture(null);
    }

    [Fact]
    public async Task NamedSkillQueuedVoiceChoiceCannotRetargetAReplacementChoiceSet()
    {
        var f = await Fixture.CreateInitializedAsync();
        var generation = 1;
        f.ViewModel.BindNamedSkillChoiceCapture(() =>
        {
            var captured = generation;
            return () => captured == generation;
        });
        var called = false;
        f.ViewModel.NamedSkillInvocationRequested += (_, _, _) => { called = true; return Task.CompletedTask; };
        f.Dispatcher.BeforeInvoke = () => generation++;
        await f.RaiseActivatedTranscriptAsync("Kora, skill choice 1", 1);
        called.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
