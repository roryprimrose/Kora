using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using System.Reflection;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task UnsupportedActivatedReplyCaptureDoesNotBindAPromptVoiceOwner()
    {
        var f = CreateVoicePrivacyFixture();
        f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance).Should().BeNull();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("question-speech")]
    [InlineData("ptt")]
    [InlineData("stop-request")]
    [InlineData("stop-release")]
    public async Task PromptCaptureRefusesEveryCompetingActivatedInputHold(string hold)
    {
        var f = await ReplyHostAsync();
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        using var draft = new PromptParameterDraft(source, f.ArtifactCatalogue, () => true);
        var stop = typeof(Kora.Application.ViewModels.MainViewModel).GetField("listeningStop",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.ViewModel)!;
        var stopping = stop.GetType().GetField("stopping", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            if (hold is "question-speech") { Field(f, "questionSpeech", true); }
            if (hold is "ptt") { Field(f, "pushToTalkHeld", 1); }
            if (hold is "stop-request") { Field(f, "listeningStopRequests", 1); }
            if (hold is "stop-release") { stopping.SetValue(stop, 1); }
            await input.Begin(draft, "topic", () => true, draft.SetValue, TestContext.Current.CancellationToken);
            input.IsActive.Should().BeFalse();
            f.Voice.StartCalls.Should().Be(0);
            draft.PreviewText.Should().BeNull();
        }
        finally
        {
            Field(f, "questionSpeech", false);
            Field(f, "pushToTalkHeld", 0);
            Field(f, "listeningStopRequests", 0);
            stopping.SetValue(stop, 0);
            f.ViewModel.UnbindPromptInputVoice(input);
            f.ViewModel.Dispose();
        }
    }

    [Fact]
    public async Task QueuedReadinessNotificationAfterUnbindingDoesNotTouchRetiredCapture()
    {
        var f = await ReplyHostAsync();
        PromptInputVoiceController? bound = null;
        f.ViewModel.PropertyChanged += (_, _) =>
        {
            if (bound is not { } current) { return; }
            bound = null;
            f.ViewModel.UnbindPromptInputVoice(current);
        };
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        bound = input;
        f.ViewModel.CommandText = "new native input";
        bound.Should().BeNull();
        input.IsActive.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task OrdinaryPushToTalkWaitsForPromptCaptureReleaseBeforeOpeningItsOwnGeneration()
    {
        var f = await ReplyHostAsync();
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        using var draft = new PromptParameterDraft(source, f.ArtifactCatalogue, () => true);
        var applied = 0;
        await input.Begin(draft, "topic", () => true, (_, _) => applied++, TestContext.Current.CancellationToken);
        var retired = f.Voice.Generation;
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = f.ViewModel.BeginPushToTalkAsync();
        try
        {
            command.IsCompleted.Should().BeFalse();
            input.IsActive.Should().BeFalse();
            f.Voice.StartCalls.Should().Be(1);
            f.Voice.RaiseTranscriptForGeneration(retired);
            applied.Should().Be(0);
        }
        finally { f.Voice.StopGate.SetResult(); }
        await command;
        f.Voice.StartCalls.Should().Be(2);
        await f.ViewModel.EndPushToTalkAsync();
        f.ViewModel.UnbindPromptInputVoice(input);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("lock")]
    [InlineData("unknown")]
    [InlineData("consent")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("question")]
    [InlineData("close")]
    public async Task PromptVoiceUsesCurrentActivatedHostAndRetiresWithoutCommandOrQuestionMixing(string change)
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<Kora.Application.Interaction.HostQuestionVoiceReplyService>.Instance)!;
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        using var draft = new PromptParameterDraft(source, f.ArtifactCatalogue, () => true);
        var applied = 0;
        await input.Begin(draft, "topic", () => true, (name, text) => { draft.SetValue(name, text); applied++; },
            TestContext.Current.CancellationToken);
        f.Voice.IsListening.Should().BeTrue();
        var generation = f.Voice.Generation;
        switch (change)
        {
            case "disable": await f.ViewModel.DisableListeningFromTrayAsync(); break;
            case "lock": PublishVoiceSession(f, WindowsSessionState.Locked); break;
            case "unknown": PublishVoiceSession(f, WindowsSessionState.Unknown); break;
            case "consent": await f.ViewModel.WithdrawVoiceConsentCommand.ExecuteAsync(); break;
            case "owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); input.Revalidate(); break;
            case "call": f.CallState.SetState(CallState.Unknown); break;
            case "question": await host.QuestionAsync(); break;
            case "close": f.ViewModel.Dispose(); break;
        }
        input.IsActive.Should().BeFalse();
        f.Voice.RaiseTranscriptForGeneration(generation);
        await input.Quiescence;
        applied.Should().Be(0);
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        f.ViewModel.UnbindPromptInputVoice(input);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task PromptAndQuestionCaptureCannotTakeEachOthersForegroundTargetOrLiteralReply()
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<Kora.Application.Interaction.HostQuestionVoiceReplyService>.Instance)!;
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        var question = await host.QuestionAsync();
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        using var draft = new PromptParameterDraft(source, f.ArtifactCatalogue, () => true);
        draft.SetValue("topic", "unchanged private input");
        var applied = 0;
        void Apply(string name, string text) { draft.SetValue(name, text); applied++; }

        await input.Begin(draft, "topic", () => true, Apply, TestContext.Current.CancellationToken);
        var promptGeneration = f.Voice.Generation;
        await replies.OpenAsync(question, () => true, _ => { }, false, TestContext.Current.CancellationToken);
        input.IsActive.Should().BeTrue();
        replies.IsActive.Should().BeFalse();
        f.Voice.Generation.Should().Be(promptGeneration);
        f.Voice.StartCalls.Should().Be(1);
        host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();

        input.Retire("Explicitly cancel prompt capture before answering the question");
        await input.Quiescence;
        await replies.OpenAsync(question, () => true, _ => { }, false, TestContext.Current.CancellationToken);
        var questionGeneration = f.Voice.Generation;
        await input.Begin(draft, "topic", () => true, Apply, TestContext.Current.CancellationToken);
        input.IsActive.Should().BeFalse();
        replies.IsActive.Should().BeTrue();
        f.Voice.Generation.Should().Be(questionGeneration);
        f.Voice.StartCalls.Should().Be(2);

        f.ReplyVoice!.Speech();
        f.Voice.RaiseTranscript("First", 1);
        await replies.Quiescence;
        var answered = host.Store.Snapshot.Questions.Single();
        answered.Draft!.Choices.Should().Equal("a");
        answered.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        applied.Should().Be(0);
        (await draft.Preview(f.ViewModel.IsPromptSourceCurrent, TestContext.Current.CancellationToken))
            .UserRequest.Should().Be("unchanged private input");
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        f.ViewModel.UnbindPromptInputVoice(input);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ComposerCaptureIsExclusiveWithPromptAndQuestionAndKeepsLiteralInItsOriginalBuffer(bool promptFirst)
    {
        var f = await ReplyHostAsync();
        f.Voice.IsLiteralDictationAvailable = true;
        using var host = new InteractionFixture();
        using var composer = DictationComposer(f);
        await using var dictation = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<Kora.Application.Interaction.HostQuestionVoiceReplyService>.Instance)!;
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        var question = await host.QuestionAsync();
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        using var draft = new PromptParameterDraft(source, f.ArtifactCatalogue, () => true);
        draft.SetValue("topic", "unchanged prompt");
        composer.Body = "unchanged composer";
        if (promptFirst)
        { await input.Begin(draft, "topic", () => true, draft.SetValue, TestContext.Current.CancellationToken); }
        else { await replies.OpenAsync(question, () => true, _ => { }, false, TestContext.Current.CancellationToken); }
        var generation = f.Voice.Generation;
        await dictation.BeginAsync(composer, () => true, TestContext.Current.CancellationToken);
        dictation.IsActive.Should().BeFalse();
        input.IsActive.Should().Be(promptFirst);
        replies.IsActive.Should().Be(!promptFirst);
        f.Voice.Generation.Should().Be(generation);
        f.Voice.StartCalls.Should().Be(1);

        input.Retire("Explicitly cancel prompt capture");
        replies.Retire("Explicitly cancel question capture");
        await input.Quiescence;
        await replies.Quiescence;
        await dictation.BeginAsync(composer, () => true, TestContext.Current.CancellationToken);
        dictation.IsActive.Should().BeTrue();
        generation = f.Voice.Generation;
        await input.Begin(draft, "topic", () => true, draft.SetValue, TestContext.Current.CancellationToken);
        await replies.OpenAsync(question, () => true, _ => { }, false, TestContext.Current.CancellationToken);
        input.IsActive.Should().BeFalse();
        replies.IsActive.Should().BeFalse();
        dictation.IsActive.Should().BeTrue();
        f.Voice.Generation.Should().Be(generation);
        f.Voice.StartCalls.Should().Be(2);

        f.Voice.RaiseTranscript("literal for composer only", 1);
        await dictation.EndAsync();
        dictation.HasReview.Should().BeTrue();
        composer.Body.Should().Be("unchanged composer");
        dictation.LiteralText.Should().Be("literal for composer only");
        dictation.Replace();
        await dictation.Quiescence;
        composer.Body.Should().Be("literal for composer only");
        host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        (await draft.Preview(f.ViewModel.IsPromptSourceCurrent, TestContext.Current.CancellationToken))
            .UserRequest.Should().Be("unchanged prompt");
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        f.ViewModel.UnbindPromptInputVoice(input);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task CurrentActivatedRecognitionSuppliesLiteralWithoutNewDictationBackendOrLocalUiRelabelling()
    {
        var f = await ReplyHostAsync();
        f.Voice.IsLiteralDictationAvailable.Should().BeFalse();
        await using var input = f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance)!;
        var duplicate = () => f.ViewModel.BindPromptInputVoice(NullLogger<PromptInputVoiceController>.Instance);
        duplicate.Should().Throw<InvalidOperationException>();
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        f.ViewModel.BindOwnedInstructionFreshness(_ => Task.FromResult(true));
        using var draft = new PromptParameterDraft(source, f.ArtifactCatalogue, () => true);
        await input.Begin(draft, "topic", () => true, draft.SetValue, TestContext.Current.CancellationToken);
        f.Voice.RaiseTranscript("Kora, lock workstation", 1);
        await input.Quiescence;
        (await draft.Preview(f.ViewModel.IsPromptSourceCurrent, TestContext.Current.CancellationToken))
            .UserRequest.Should().Be("Kora, lock workstation");
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.UnbindPromptInputVoice(input);
        f.ViewModel.UnbindPromptInputVoice(input);
        f.ViewModel.Dispose();
    }
}
