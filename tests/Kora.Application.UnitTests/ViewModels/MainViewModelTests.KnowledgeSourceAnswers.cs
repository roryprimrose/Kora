using AwesomeAssertions;
using Kora.Application.KnowledgeSources;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Communication;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NamedAnswerCannotDisplacePendingQuestionOrCreateAutomaticModelFallback()
    {
        var f = new Fixture();
        await using var pendingDetails = new PendingDetailsQuestion(f);
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        await f.ViewModel.InitializeAsync();
        await pendingDetails.PresentAsync();
        var calls = 0;
        f.ViewModel.KnowledgeSourceAnswerRequested += (_, _) => { calls++; return Task.CompletedTask; };
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("answer using source \"Team\": alpha");
        calls.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedSourceAnswerTypedAndCurrentNameVoiceReachSameReviewWithUnchangedQuestion(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.LocalUser);
        var received = new List<KnowledgeSourceAnswerCommand>();
        Func<bool>? current = null;
        f.ViewModel.KnowledgeSourceAnswerRequested += (command, eligible) =>
        {
            eligible().Should().BeTrue();
            HostActivity.RequireCurrent().Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            received.Add(command);
            current = eligible;
            return Task.CompletedTask;
        };
        const string input = "answer using source \"Team\":   exact New question?  ";
        if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + input, 1); }
        else { await f.RunAsync(input); }
        received.Should().ContainSingle();
        received[0].Name.Should().Be("Team");
        received[0].Question.Should().Be("  exact New question?  ");

        f.ViewModel.RevokeSessionPresentation(new(Guid.NewGuid()));
        current!().Should().BeFalse();
    }

    [Fact]
    public async Task UnactivatedVoiceUnavailableRouteMalformedCommandAndUnknownOwnershipCannotDispatchNamedAnswer()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("answer using source \"Team\": alpha");
        var calls = 0;
        f.ViewModel.KnowledgeSourceAnswerRequested += (_, _) => { calls++; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync("answer using source \"Team\": alpha", 1);
        await f.RunAsync("answer using source all");
        f.ViewModel.BindClipboardOwnershipGate(() => false);
        await f.RunAsync("answer using source \"Team\": alpha");
        calls.Should().Be(0);

    }

    [Theory]
    [InlineData("ordinary question", false, false)]
    [InlineData("answer using source", true, true)]
    [InlineData("answer using source Team: question", true, true)]
    [InlineData("answer using source \"\": question", true, true)]
    [InlineData("answer using source \"Team\": !!!", true, true)]
    [InlineData("answer using source \"Team\"", true, true)]
    [InlineData("Nova, answer using source \"Team\": Alpha?", true, false)]
    public void NamedAnswerGrammarPreservesReservedInputWithoutGuessingSourceOrQuestion(string input, bool matched, bool invalid)
    {
        var command = KnowledgeSourceAnswerCommand.Parse(input, "Nova");
        (command is not null).Should().Be(matched);
        if (command is not null) { command.Invalid.Should().Be(invalid); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedAnswerUnavailablePrefixNeverManufacturesNameAuthority(bool prefixed)
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.KnowledgeSourceAnswerRequested += (_, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync((prefixed ? "Kora, " : "") + "answer using source \"Team\": alpha");
        calls.Should().Be(prefixed ? 0 : 1);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedAnswerProtectedCallRetainsOriginalTypedOrVoiceRefusal(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.CallState.SetState(CallState.Active);
        await f.Dispatcher.LastInvocation;
        var calls = 0;
        f.ViewModel.KnowledgeSourceAnswerRequested += (_, _) => { calls++; return Task.CompletedTask; };
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, answer using source \"Team\": alpha", 1); }
        else { await f.RunAsync("answer using source \"Team\": alpha"); }
        calls.Should().Be(0);
    }


}
