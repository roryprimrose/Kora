using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiredSettingsCannotReplaceAnOriginalQuestionOrApproval(bool approval)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = approval ? null : new("Which?", ["One", "Two"]);
        f.Reasoner.Action = approval ? BuiltInAction.EnableHostedModels : null;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var actions = f.ViewModel.ResponseActions;
        var choices = f.ViewModel.ModelQuestionChoices;
        var requests = f.Reasoner.Requests.Count;
        var spoken = f.TextToSpeech.SpokenText;

        await f.RunAsync("reset fixed read pending lifetime");

        f.ViewModel.Transcript.Should().Contain("retired");
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.ResponseActions.Should().BeSameAs(actions);
        f.ViewModel.ModelQuestionChoices.Should().BeSameAs(choices);
        f.Reasoner.Requests.Should().HaveCount(requests);
        f.TextToSpeech.SpokenText.Should().Be(spoken);
        f.ViewModel.Dispose();
    }
    [Theory]
    [InlineData(false, "session diagnostics plan 12345678-1234-1234-abcd-123456789012 version,runtimes")]
    [InlineData(true, "session diagnostics plan 12345678-1234-1234-abcd-123456789012 version,runtimes")]
    [InlineData(false, "queue enqueue 12345678-1234-1234-abcd-123456789012 1 0 12345678-1234-1234-abcd-123456789013 12345678-1234-1234-abcd-123456789014 version")]
    [InlineData(true, "queue dispatch 12345678-1234-1234-abcd-123456789012 1 0")]
    [InlineData(false, "show fixed read queue settings")]
    [InlineData(true, "set fixed read pending lifetime to 60 minutes")]
    public async Task RetiredDiagnosticProductInputNeverReachesModelSpeechOrQueueAdmission(bool voice, string input)
    {
        var f = await Fixture.CreateInitializedAsync();
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + input, 1); }
        else { await f.RunAsync(input); }
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.ResponseTitle.Should().NotBe("Session diagnostics plan prepared.");
    }
}
