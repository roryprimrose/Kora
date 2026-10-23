using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Presentation;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string MarkdownAnswer = "- [ ] Deploy Alpha\r\n- [x] Verify Beta\r\n- [X] Finish Gamma\r\n";

    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("voice")]
    public async Task FinalModelAnswerDetailsPreserveExactAnswerThroughExistingOriginalUserRoutes(string route)
    {
        var f = CreateSummaryFixture(SpeechProviderIds.Windows);
        f.Reasoner.Response = new(MarkdownAnswer, null);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("prepare a deployment checklist");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var visibleBody = f.ViewModel.ResponseBody;
        visibleBody.Should().Contain("Generated locally; verify important details.").And.EndWith(MarkdownAnswer);
        var presenter = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(presenter);
        var speechCalls = f.TextToSpeech.SpeakCalls;
        if (string.Equals(route, "native", StringComparison.Ordinal))
        { await f.ViewModel.OpenCurrentResponseDetailsAsync(); }
        else if (string.Equals(route, "voice", StringComparison.Ordinal))
        { await f.RaiseActivatedTranscriptAsync("Kora, open current response details", 1); }
        else { await f.RunAsync("open current response details"); }
        presenter.Calls.Should().Be(1);
        presenter.Content!.Kind.Should().Be(DetailContentKind.Markdown);
        presenter.Content.Origin.Should().Be(DetailContentOrigin.FinalizedResponse);
        presenter.Content.Source.Should().Be(MarkdownAnswer);
        presenter.Content.SessionSource.Should().NotBeNull();
        presenter.Content.Provenance.Should().Contain("Generated locally");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(visibleBody);
        f.ViewModel.Transcript.Should().Be("Exact current response details opened.");
        f.TextToSpeech.SpeakCalls.Should().Be(speechCalls);
        f.Reasoner.Requests.Should().ContainSingle("Details never redispatches the model");
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task HostClassifiesAnswerByValidatedSourceRatherThanMarkdownMarkers()
    {
        var f = CreateSummaryFixture(SpeechProviderIds.Windows);
        f.Reasoner.Response = new("An ordinary answer without any Markdown markers.", null);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("explain the deployment");
        await f.ViewModel.ActiveReasoningTask!;
        var presenter = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(presenter);
        await f.ViewModel.OpenCurrentResponseDetailsAsync();
        presenter.Content!.Kind.Should().Be(DetailContentKind.Markdown);
        presenter.Content.Source.Should().Be(f.Reasoner.Response.Answer);
        await f.RunAsync("what power action is pending");
        await f.ViewModel.OpenCurrentResponseDetailsAsync();
        presenter.Content!.Kind.Should().Be(DetailContentKind.PlainText);
        presenter.Content.Source.Should().Be(f.ViewModel.ResponseTitle + Environment.NewLine + f.ViewModel.ResponseBody);
        presenter.Content.Source.Should().NotContain("An ordinary answer");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiringOrReplacingFinalModelAnswerCannotRepublishItsMarkdownSource(bool replace)
    {
        var f = CreateSummaryFixture(SpeechProviderIds.Windows);
        f.Reasoner.Response = new(MarkdownAnswer, null);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("prepare a deployment checklist");
        await f.ViewModel.ActiveReasoningTask!;
        var presenter = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(presenter);
        if (replace) { await f.RunAsync("what power action is pending"); }
        else { f.ViewModel.RetireCurrentResponseSpeech(); }
        await f.ViewModel.OpenCurrentResponseDetailsAsync();
        if (replace)
        {
            presenter.Content!.Kind.Should().Be(DetailContentKind.PlainText);
            presenter.Content.Source.Should().NotContain("Deploy Alpha");
        }
        else { presenter.Calls.Should().Be(0); }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InvalidFinalAnswerNeverCreatesMarkdownDetailsSource(string answer)
    {
        var f = CreateSummaryFixture(SpeechProviderIds.Windows);
        f.Reasoner.Response = new(answer, null);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("prepare a deployment checklist");
        await f.ViewModel.ActiveReasoningTask!;
        var presenter = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(presenter);
        await f.ViewModel.OpenCurrentResponseDetailsAsync();
        presenter.Calls.Should().Be(0);
    }
}
