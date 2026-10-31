using System.Reflection;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Communication;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("busy")]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("dictation")]
    [InlineData("prompt")]
    [InlineData("reply")]
    [InlineData("editor")]
    [InlineData("prompt-review")]
    [InlineData("ptt")]
    [InlineData("input")]
    [InlineData("private")]
    [InlineData("call")]
    public async Task NoticeQuestionGesturePreservesEarlierDecisionAndCapturePrecedence(string change)
    {
        var f = new Fixture(enableQuestionReplies: true);
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Synthetic headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        await f.ViewModel.InitializeAsync();
        using var host = new InteractionFixture();
        using var composer = DictationComposer(f);
        f.Voice.IsLiteralDictationAvailable = true;
        var original = f.ViewModel.CaptureQuestionNoticeNavigationEligibility();
        original().Should().BeTrue();
        await using var dictation = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<Kora.Application.Interaction.HostQuestionVoiceReplyService>.Instance)!;
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        var retired = EditorVoiceTarget();
        retired.Retire();
        f.ViewModel.PublishCustomizationVoiceTarget(retired);
        f.ViewModel.CaptureQuestionNoticeNavigationEligibility()().Should().BeTrue();
        f.ViewModel.PublishCustomizationVoiceTarget(null);
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        using var draft = new PromptParameterDraft(source, f.ArtifactCatalogue, () => true);
        switch (change)
        {
            case "busy": SetResponseProperty(f.ViewModel, nameof(MainViewModel.IsBusy), true); break;
            case "grant": SetResponseProperty(f.ViewModel, nameof(MainViewModel.IsGrantEditorVisible), true); break;
            case "question":
                f.Reasoner.Question = new("Which target?", ["One", "Two"]);
                await f.RunAsync("choose");
                await f.ViewModel.ActiveReasoningTask!;
                break;
            case "dictation": await dictation.BeginAsync(composer, () => true, TestContext.Current.CancellationToken); break;
            case "prompt": await input.Begin(draft, "topic", () => true, draft.SetValue, TestContext.Current.CancellationToken); break;
            case "reply": await replies.OpenAsync(await host.QuestionAsync(), () => true, _ => { }, false, TestContext.Current.CancellationToken); break;
            case "editor": f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget()); break;
            case "prompt-review": f.ViewModel.BindRetainedAnswerReadDecisionGate(() => false); break;
            case "ptt":
                typeof(MainViewModel).GetField("pushToTalkHeld", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(f.ViewModel, 1);
                break;
            case "input": f.ViewModel.CommandText = "independent input"; break;
            case "private": f.Session.IsUnlocked = false; break;
            case "call": f.CallState.SetState(CallState.Active); break;
        }
        original().Should().BeFalse();
        f.ViewModel.CaptureQuestionNoticeNavigationEligibility()().Should().Be(change is "input");
        input.Retire("test completed");
        replies.Retire("test completed");
        dictation.Retire("test completed");
        await input.Quiescence;
        await replies.Quiescence;
        await dictation.Quiescence;
        f.ViewModel.UnbindPromptInputVoice(input);
        f.ViewModel.Dispose();
    }
}
