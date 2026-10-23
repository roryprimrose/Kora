using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Artifacts;
using Kora.Core.Communication;
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
