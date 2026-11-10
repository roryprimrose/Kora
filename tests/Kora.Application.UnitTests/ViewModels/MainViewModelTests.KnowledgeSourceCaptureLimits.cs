using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalTypedAndActivatedLimitsCommandsUseSameRouteAndRetireWithSourceAuthority(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.LocalUser);
        var received = new List<KnowledgeSourceCaptureLimitsCommand>();
        Func<bool>? current = null;
        f.ViewModel.KnowledgeSourceCaptureLimitsRequested += (command, eligible) =>
        {
            eligible().Should().BeTrue();
            HostActivity.RequireCurrent().Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            received.Add(command);
            current = eligible;
            return Task.CompletedTask;
        };
        foreach (var input in new[]
        {
            "source capture limits get \"Team\"",
            "source capture limits lower \"Team\" 2 100 200",
            "source capture limits confirm",
            "source capture limits reset \"Team\"",
            "source capture limits cancel",
        })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + input, 1); }
            else { await f.RunAsync(input); }
        }
        received.Select(c => c.Operation).Should().Equal("get", "lower", "confirm", "reset", "cancel");
        received[1].Name.Should().Be("Team");
        received[1].Limits!.MaximumCombinedBytes.Should().Be(200);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.RevokeSessionPresentation(new(Guid.NewGuid()));
        current!().Should().BeFalse();
    }

    [Fact]
    public async Task UnavailableMalformedUnactivatedAndForeignOwnershipLimitsNeverDispatchOrFallBackToModel()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("source capture limits get \"Team\"");
        var calls = 0;
        f.ViewModel.KnowledgeSourceCaptureLimitsRequested += (_, _) => { calls++; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync("source capture limits get \"Team\"", 1);
        await f.RunAsync("source capture limits lower \"Team\" 33 1 1");
        f.ViewModel.BindClipboardOwnershipGate(() => false);
        await f.RunAsync("source capture limits get \"Team\"");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceLimitsProtectedCallRefusesOriginalTypedAndActivatedVoice(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.CallState.SetState(CallState.Active);
        await f.Dispatcher.LastInvocation;
        var calls = 0;
        f.ViewModel.KnowledgeSourceCaptureLimitsRequested += (_, _) => { calls++; return Task.CompletedTask; };
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, source capture limits confirm", 1); }
        else { await f.RunAsync("source capture limits confirm"); }
        calls.Should().Be(0);
    }

    [Fact]
    public async Task SourceLimitsUnavailableAssistantPrefixCannotCreateNameAuthority()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.KnowledgeSourceCaptureLimitsRequested += (_, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync("Kora, source capture limits get \"Team\"");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceLimitsCannotDisplacePendingQuestion()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        f.Reasoner.Question = new("Keep this exact question?", ["one", "two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("please do this work");
        await f.ViewModel.ActiveReasoningTask!;
        var calls = 0;
        f.ViewModel.KnowledgeSourceCaptureLimitsRequested += (_, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync("source capture limits get \"Team\"");
        calls.Should().Be(0);
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
    }
}
