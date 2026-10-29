using AwesomeAssertions;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Communication;
using Microsoft.Extensions.Logging.Abstractions;
using Kora.Application.Interaction;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Shared_native_preference_starts_off_saves_explicitly_and_does_not_open_a_microphone()
    {
        var f = new Fixture(enableConversationalReplies: true);
        f.ViewModel.AreConversationalRepliesEnabled.Should().BeFalse();
        f.ViewModel.ConversationalReplyStatus.Should().Contain("Off (default)");
        f.ViewModel.CanChangeConversationalReplies.Should().BeFalse();
        f.ViewModel.BindConversationalReplyNativeLifetime(static () => true);
        f.ViewModel.CanChangeConversationalReplies.Should().BeTrue();
        await f.ViewModel.EnableConversationalRepliesCommand.ExecuteAsync();
        f.ViewModel.AreConversationalRepliesEnabled.Should().BeTrue();
        f.ViewModel.ConversationalReplyStatus.Should().Contain("On.");
        f.Voice.IsListening.Should().BeFalse();
        await f.ViewModel.DisableConversationalRepliesCommand.ExecuteAsync();
        f.ConversationalPreferences.Value.Should().BeFalse();
        f.ViewModel.Dispose();
        f.ConversationalConfiguration!.Observe();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("native")]
    [InlineData("owner")]
    [InlineData("write")]
    [InlineData("denied")]
    [InlineData("rebind")]
    [InlineData("busy")]
    [InlineData("pending")]
    public async Task Preference_requires_current_native_original_input_and_exposes_failure_without_listening(string boundary)
    {
        var f = new Fixture(enableConversationalReplies: boundary is not "missing",
            conversationalReadFailure: boundary is "corrupt" ? new InvalidDataException() : null);
        f.ViewModel.BindConversationalReplyNativeLifetime(() => boundary is not "native");
        if (boundary is "pending")
        {
            f.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
            f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.EnableHostedModels;
            await f.ViewModel.InitializeAsync();
            await f.RunAsync("lock this workstation please");
            await f.ViewModel.ActiveReasoningTask!;
            f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        }
        if (boundary is "missing" or "corrupt") { f.ViewModel.AreConversationalRepliesEnabled.Should().BeFalse(); }
        if (boundary is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (boundary is "missing" or "corrupt" or "native" or "owner" or "pending")
        { f.ViewModel.CanChangeConversationalReplies.Should().BeFalse(); }
        if (boundary is "write") { f.ConversationalPreferences.Failure = new IOException(); }
        if (boundary is "denied")
        {
            f.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Requested
                    && string.Equals(item.ActionId, "configuration.conversational-replies", StringComparison.Ordinal))
                { f.ViewModel.BindConversationalReplyNativeLifetime(static () => false); }
            };
        }
        if (boundary is "rebind")
        {
            f.ManualCallStore.AfterOperation = () => f.ViewModel.BindConversationalReplyNativeLifetime(static () => false);
        }
        Task? duplicate = null;
        if (boundary is "busy")
        {
            f.ManualCallStore.BeforeOperation = () =>
            {
                f.ManualCallStore.BeforeOperation = null;
                f.ViewModel.CanChangeConversationalReplies.Should().BeFalse();
                duplicate = f.ViewModel.DisableConversationalRepliesCommand.ExecuteAsync();
            };
        }
        await f.ViewModel.EnableConversationalRepliesCommand.ExecuteAsync();
        if (duplicate is not null) { await duplicate; }
        f.Voice.IsListening.Should().BeFalse();
        if (boundary is "missing" or "corrupt" or "write" or "rebind")
        { f.ViewModel.ConversationalReplyStatus.Should().Contain("unavailable"); }
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Actual_host_preference_controls_exact_playback_reply_and_replacement_retires_it()
    {
        var f = new Fixture(enableConversationalReplies: true, enableQuestionReplies: true);
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Synthetic")];
        f.Voice.DefaultMicrophoneId = "mic";
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        f.ViewModel.BindConversationalReplyNativeLifetime(static () => true);
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.EnableConversationalRepliesCommand.ExecuteAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await replies.OpenConversationalAsync(question, static () => true, _ => { }, TestContext.Current.CancellationToken);
        f.Voice.IsListening.Should().BeTrue();
        f.TextToSpeech.SpokenText.Should().Be(question.Spec.Text);
        f.ConversationalConfiguration!.Observe();
        f.Voice.IsListening.Should().BeFalse();
        await replies.Quiescence;
        f.ViewModel.Dispose();
    }
}
