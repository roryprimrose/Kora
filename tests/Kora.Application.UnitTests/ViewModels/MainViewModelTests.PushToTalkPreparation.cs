using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Platform;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PushToTalkPreparationKeepsOriginalHeldIntentUntilPriorCaptureIsReleased(
        bool composerCapture, bool releaseBeforeCleanup)
    {
        var f = await ReplyHostAsync();
        f.Voice.IsLiteralDictationAvailable = true;
        using var composer = DictationComposer(f);
        await using var dictation = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        using var draft = new PromptParameterDraft(source, f.ArtifactCatalogue, () => true);
        var applied = 0;
        if (composerCapture)
        {
            await dictation.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        }
        else
        {
            await input.Begin(draft, "topic", () => true, (_, _) => applied++, TestContext.Current.CancellationToken);
        }
        f.Voice.StartCalls.Should().Be(1);
        var retired = f.Voice.Generation;
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (composerCapture) { dictation.Retire("Original dictate gesture cancelled."); }
        else { input.Retire("Original prompt capture cancelled."); }
        f.ViewModel.BeginPushToTalkCommand.CanExecute(null).Should().BeTrue();
        var command = f.ViewModel.BeginPushToTalkCommand.ExecuteAsync();
        try
        {
            command.IsCompleted.Should().BeFalse("prior capture cleanup must remain joined");
            f.ViewModel.CanPushToTalk.Should().BeFalse();
            dictation.IsActive.Should().BeFalse();
            input.IsActive.Should().BeFalse();
            if (releaseBeforeCleanup)
            {
                await f.ViewModel.EndPushToTalkCommand.ExecuteAsync();
                command.IsCompleted.Should().BeFalse("release cannot certify prior resource quiescence");
            }
            await f.ViewModel.BeginPushToTalkAsync();
            command.IsCompleted.Should().BeFalse("another activation cannot replace the original cleanup owner");
            f.Voice.RaiseTranscriptForGeneration(retired);
            f.Voice.StartCalls.Should().Be(1);
            f.Session.LockCalls.Should().Be(0);
            applied.Should().Be(0);
            composer.Body.Should().BeEmpty();
        }
        finally { f.Voice.StopGate.SetResult(); }
        await command;

        f.Voice.StartCalls.Should().Be(releaseBeforeCleanup ? 1 : 2,
            "a released gesture cannot open command capture after prior cleanup");
        f.ViewModel.IsListening.Should().Be(!releaseBeforeCleanup);
        f.Voice.IsCaptureQuiescent.Should().Be(releaseBeforeCleanup);
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.VoiceConsent.Consent.Should().BeTrue();
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        f.TextToSpeech.SpokenText.Should().BeNull();
        applied.Should().Be(0);
        composer.Body.Should().BeEmpty();

        await f.ViewModel.EndPushToTalkCommand.ExecuteAsync();
        await f.ViewModel.BeginPushToTalkCommand.ExecuteAsync();
        f.Voice.StartCalls.Should().Be(releaseBeforeCleanup ? 2 : 3);
        f.ViewModel.IsListening.Should().BeTrue();
        await f.ViewModel.BeginPushToTalkAsync();
        f.Voice.StartCalls.Should().Be(releaseBeforeCleanup ? 2 : 3);
        await f.ViewModel.EndPushToTalkCommand.ExecuteAsync();
        await f.Voice.RaiseTranscriptAsync("help", 1);
        f.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
        f.ViewModel.IsListening.Should().BeFalse();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.ViewModel.UnbindPromptInputVoice(input);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PushToTalkPreparationCannotOutliveListeningStopOrPrivacyClosure(bool privacyClosure)
    {
        var f = await ReplyHostAsync();
        f.Voice.IsLiteralDictationAvailable = true;
        using var composer = DictationComposer(f);
        await using var dictation = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        await dictation.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        var retired = f.Voice.Generation;
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dictation.Retire("Original dictate gesture cancelled.");
        var command = f.ViewModel.BeginPushToTalkCommand.ExecuteAsync();
        Task closure;
        if (privacyClosure)
        {
            PublishVoiceSession(f, WindowsSessionState.Locked);
            closure = f.ViewModel.PrivacyClosureTask;
        }
        else { closure = f.ViewModel.DisableListeningFromTrayAsync(); }
        try
        {
            command.IsCompleted.Should().BeFalse();
            closure.IsCompleted.Should().BeFalse();
            f.Voice.StartCalls.Should().Be(1);
        }
        finally { f.Voice.StopGate.SetResult(); }
        await Task.WhenAll(command, closure);
        f.Voice.RaiseTranscriptForGeneration(retired);
        f.Voice.StartCalls.Should().Be(1);
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.ViewModel.IsListening.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.VoiceConsent.Consent.Should().BeTrue();
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        composer.Body.Should().BeEmpty();
        f.ViewModel.Dispose();
    }
}
