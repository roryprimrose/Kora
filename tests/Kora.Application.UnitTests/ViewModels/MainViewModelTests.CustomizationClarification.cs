using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("voice")]
    [InlineData("native")]
    [InlineData("unsupported")]
    [InlineData("withdrawal")]
    [InlineData("edit")]
    [InlineData("cancel")]
    public async Task Editor_question_capture_is_scoped_to_busy_original_request_and_never_global_routing(string state)
    {
        var f = state is "unsupported" ? await Fixture.CreateInitializedAsync() : await ReplyHostAsync();
        using var host = new InteractionFixture();
        var unrelated = await host.QuestionAsync();
        var target = EditorVoiceTarget();
        f.ViewModel.PublishCustomizationVoiceTarget(target);
        var calls = 0;
        f.ViewModel.BindCustomizationVoice(async request =>
        {
            calls++;
            request.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            var exact = true;
            await using var question = f.ViewModel.CreateCustomizationClarification(request, () => exact,
                NullLogger.Instance, TestContext.Current.CancellationToken);
            try
            {
                Action duplicate = () => f.ViewModel.CreateCustomizationClarification(request, () => true,
                    NullLogger.Instance, TestContext.Current.CancellationToken);
                duplicate.Should().Throw<InvalidOperationException>();
                if (state is not ("native" or "unsupported")) { question.StartVoice(); }
                if (state is "withdrawal") { f.ViewModel.RetireQuestionVoiceReply(); }
                else if (state is "edit")
                {
                    exact = false;
                    f.ViewModel.CommandText = "Independent input";
                }
                else if (state is "cancel") { question.Choose(CustomizationVoiceChoice.Cancel); }
                else if (state is "voice") { f.Voice.RaiseTranscript("Discuss this artifact", 1); }
                else { question.Choose(CustomizationVoiceChoice.Discuss); }
                (await question.Completion).Should().Be(state is "withdrawal" or "edit" or "cancel"
                    ? CustomizationVoiceChoice.Cancel : CustomizationVoiceChoice.Discuss);
                request.IsCurrent.Should().Be(state is "voice" or "native" or "unsupported");
                f.Voice.IsCaptureQuiescent.Should().BeTrue();
            }
            finally { f.ViewModel.ReleaseCustomizationClarification(question); f.ViewModel.ReleaseCustomizationClarification(question); }
        });
        await f.RaiseActivatedTranscriptAsync("make this clearer", 1);
        calls.Should().Be(1);
        f.ViewModel.IsBusy.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        host.Store.Snapshot.Questions.Single().Key.Should().Be(unrelated.Key);
        host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Reentrant_release_during_property_delivery_leaves_no_question_observer()
    {
        var f = await ReplyHostAsync();
        CustomizationVoiceClarification? question = null;
        f.ViewModel.PropertyChanged += (_, _) =>
        {
            if (question is not null) { f.ViewModel.ReleaseCustomizationClarification(question); }
        };
        f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget());
        f.ViewModel.BindCustomizationVoice(async request =>
        {
            await using var owned = f.ViewModel.CreateCustomizationClarification(request, static () => true,
                NullLogger.Instance, TestContext.Current.CancellationToken);
            question = owned;
            f.ViewModel.CommandText = "New independent input";
            owned.Choose(CustomizationVoiceChoice.Cancel);
            await owned.Completion;
            question = null;
        });
        await f.RaiseActivatedTranscriptAsync("make this clearer", 1);
        f.ViewModel.Dispose();
    }
}
