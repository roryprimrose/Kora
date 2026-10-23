using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Artifacts;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Platform;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
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
