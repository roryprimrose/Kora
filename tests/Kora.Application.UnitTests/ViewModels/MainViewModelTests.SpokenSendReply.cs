using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task SpokenSendSettingsDefaultOffAndNativeOptInNeverOpenCapture()
    {
        var f = await ReplyHostAsync();
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        f.ViewModel.SpokenSendReplyStatus.Should().Contain("unavailable");
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        f.ViewModel.AreSpokenSendRepliesEnabled.Should().BeFalse();
        f.ViewModel.SpokenSendReplyStatus.Should().Contain("Off (default)");
        f.ViewModel.CanChangeSpokenSendReplies.Should().BeFalse();
        await f.ViewModel.EnableSpokenSendRepliesCommand.ExecuteAsync();
        setting.Store.Writes.Should().Be(0);
        f.ViewModel.BindSpokenSendReplyNativeLifetime(static () => true);
        await f.ViewModel.EnableSpokenSendRepliesCommand.ExecuteAsync();
        f.ViewModel.AreSpokenSendRepliesEnabled.Should().BeTrue();
        f.ViewModel.SpokenSendReplyStatus.Should().Contain("On.");
        await f.ViewModel.DisableSpokenSendRepliesCommand.ExecuteAsync();
        f.ViewModel.AreSpokenSendRepliesEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    [InlineData("native-lost")]
    [InlineData("native-replaced")]
    public async Task SpokenSendSettingFailuresRemainUnavailableWithoutOpeningCapture(string boundary)
    {
        var f = await ReplyHostAsync();
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        f.ViewModel.AreSpokenSendRepliesEnabled.Should().BeFalse();
        f.ViewModel.SessionRequestVoiceReplyStatus.Should().Be("Microphone closed.");
        f.ViewModel.CanChangeSpokenSendReplies.Should().BeFalse();
        if (boundary is "load") { setting.Store.LoadFailure = new UnauthorizedAccessException("Synthetic"); }
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        var native = true;
        f.ViewModel.BindSpokenSendReplyNativeLifetime(() => native);
        if (boundary is "save") { setting.Store.SaveFailure = new IOException("Synthetic"); }
        setting.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.ViewModel.CanChangeSpokenSendReplies.Should().BeFalse();
            if (boundary is "native-lost") { native = false; }
            if (boundary is "native-replaced") { f.ViewModel.BindSpokenSendReplyNativeLifetime(static () => true); }
        };
        if (boundary is "save")
        {
            var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.ViewModel.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(f.ViewModel.ResponseTitle), StringComparison.Ordinal)
                    && string.Equals(f.ViewModel.ResponseTitle, "Spoken send reply setting not confirmed.", StringComparison.Ordinal))
                { failure.TrySetResult(); }
            };
            f.ViewModel.EnableSpokenSendRepliesCommand.Execute(null);
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }
        else { await f.ViewModel.EnableSpokenSendRepliesCommand.ExecuteAsync(); }
        f.ViewModel.AreSpokenSendRepliesEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.ResponseTitle.Should().Be(boundary is "load" ? "Spoken send reply setting unavailable."
            : boundary is "save" ? "Spoken send reply setting not confirmed." : "Spoken send reply setting denied.");
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("off")]
    [InlineData("typed")]
    [InlineData("no-source")]
    [InlineData("foreign-review")]
    [InlineData("foreign-body")]
    [InlineData("occupied")]
    [InlineData("unnamed")]
    public async Task SpokenSendRoutingRejectsNonoriginalOrConflictingReviewWithoutCapture(string boundary)
    {
        var f = await RoutingReplyHostAsync();
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        setting.Store.Value = boundary is "off" ? "Off" : "On";
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        if (boundary is "no-source")
        {
            var review = RoutingVoiceReview("original", RequestOrigin.ActivatedVoice);
            f.ViewModel.CreateSessionRoutingSpokenSendReply(review, new(review, null), static () => true).Should().BeNull();
        }
        else
        {
            f.ViewModel.BindSessionRouting(async (body, origin, admitted) =>
            {
                var review = RoutingVoiceReview(boundary is "foreign-body" ? "other" : body,
                    boundary is "typed" ? RequestOrigin.LocalUi : origin);
                var target = boundary is "unnamed" ? review.Candidates[0].Entry with { Metadata = null } : review.Candidates[0].Entry;
                var selection = new Kora.Application.Hosting.SessionRouting.SessionRoutingSelection(
                    boundary is "foreign-review" ? RoutingVoiceReview(body, origin) : review, target);
                Kora.Application.Hosting.SessionRouting.SessionRoutingVoiceReply? held = null;
                if (boundary is "occupied") { held = f.ViewModel.CreateSessionRoutingVoiceReply(review, review.Candidates, null, admitted); }
                if (boundary is "foreign-review" or "foreign-body" or "occupied")
                {
                    FluentActions.Invoking(() => f.ViewModel.CreateSessionRoutingSpokenSendReply(review, selection, admitted))
                        .Should().Throw<InvalidOperationException>();
                }
                else if (boundary is "unnamed")
                {
                    await using var reply = f.ViewModel.CreateSessionRoutingSpokenSendReply(review, selection, admitted)!;
                    var operation = reply.StartAsync();
                    f.TextToSpeech.SpokenText.Should().Be("Send to New conversation: original?");
                    f.Voice.RaiseTranscript("cancel", 1);
                    await operation;
                    f.ViewModel.ReleaseSessionRoutingVoiceReply(reply);
                }
                else { f.ViewModel.CreateSessionRoutingSpokenSendReply(review, selection, admitted).Should().BeNull(); }
                if (held is not null)
                {
                    await held.DisposeAsync();
                    f.ViewModel.ReleaseSessionRoutingVoiceReply(held);
                }
                return null;
            });
            await f.RaiseActivatedTranscriptAsync("Kora, original", 1);
        }
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(true, "send")]
    [InlineData(true, "cancel")]
    [InlineData(true, "send please")]
    [InlineData(false, "send")]
    public async Task SpokenSendRequestReviewUsesExactOriginalAndOnlyOneJoinedAnswer(bool foreground, string answer)
    {
        var f = await RoutingReplyHostAsync();
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        setting.Store.Value = "On";
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        var store = BindRequestCommands(f);
        var target = new ForegroundConversationTarget(store.Session, static () => true);
        f.ViewModel.PublishForegroundConversationTarget(target);
        var presented = false;
        var closed = false;
        var ready = ObserveSpokenSendCapture(f);
        f.ViewModel.BindSpokenSendReview(admitted =>
        {
            admitted().Should().BeTrue();
            presented = true;
            f.ViewModel.PublishForegroundConversationTarget(null);
            return (() => admitted(), () => closed = true);
        });
        const string body = "  original e\u0301\r\nbody  ";
        var command = foreground ? "Kora, ask this conversation " + body
            : "Kora, session request review " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 " + body;
        if (!foreground)
        {
            await f.RunAsync("session rename " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 \"Build plan\"");
            command = "Kora, in Build plan, " + body;
        }
        var operation = f.RaiseActivatedTranscriptAsync(command, 1);
        await Task.WhenAny(operation, ready).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        presented.Should().BeTrue(f.ViewModel.ResponseBody);
        ready.IsCompleted.Should().BeTrue(f.ViewModel.SessionRequestVoiceReplyStatus);
        f.TextToSpeech.SpokenText.Should().EndWith(": " + body + "?");
        f.Voice.StartedPhrases.Should().Equal("send", "cancel");
        store.RequestRetentions.Should().Be(0);
        f.Voice.RaiseTranscript(answer, 1);
        await operation;
        closed.Should().BeTrue();
        store.RequestRetentions.Should().Be(answer is "send" ? 1 : 0);
        if (answer is "send")
        {
            store.RequestMessage!.Body.Should().Be(body);
            store.RequestMessage.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        }
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("off")]
    [InlineData("typed")]
    [InlineData("no-presenter")]
    [InlineData("hidden")]
    [InlineData("speech-failure")]
    [InlineData("no-release")]
    public async Task SpokenSendNeverOpensForOffTypedOrUnavailableExactPresentation(string boundary)
    {
        var f = boundary is "no-release" ? await ReplyHostAsync() : await RoutingReplyHostAsync();
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        setting.Store.Value = boundary is "off" ? "Off" : "On";
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        var store = BindRequestCommands(f);
        f.ViewModel.PublishForegroundConversationTarget(new(store.Session, static () => true));
        if (boundary is not "no-presenter")
        { f.ViewModel.BindSpokenSendReview(admitted => (() => boundary is not "hidden" && admitted(), static () => { })); }
        if (boundary is "speech-failure") { f.TextToSpeech.SpeakException = new IOException("Synthetic"); }
        if (boundary is "typed") { await f.RunAsync("ask this conversation original"); }
        else { await f.RaiseActivatedTranscriptAsync("Kora, ask this conversation original", 1); }
        f.Voice.StartCalls.Should().Be(boundary is "typed" ? 0 : 1, "only the original command capture may run");
        store.RequestRetentions.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpokenSendRequestCannotReplaceAnExistingOriginalVoiceOwner()
    {
        var f = await RoutingReplyHostAsync();
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        setting.Store.Value = "On";
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        var store = BindRequestCommands(f);
        f.ViewModel.BindSessionRouting(async (_, _, _) =>
        {
            var command = Kora.Core.Commands.SessionCommand.Parse("session request review "
                + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 -- nested original", "Kora")!;
            await f.ViewModel.ExecuteSessionCommandAsync(command, SecurityAuditInitiator.VoiceCommand,
                static () => true, createVoiceRequest: _ => throw new InvalidOperationException("The existing owner must be retained."));
            f.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
            f.ViewModel.ResponseBody.Should().Contain("Another original request still owns voice capture.");
            store.RequestRetentions.Should().Be(0);
            return null;
        });
        await f.RaiseActivatedTranscriptAsync("Kora, original", 1);
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("target")]
    [InlineData("native")]
    [InlineData("settings-refresh")]
    public async Task SpokenSendRevocationOrNativeChoiceJoinsBeforeAnySubmission(string boundary)
    {
        var f = await RoutingReplyHostAsync();
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        setting.Store.Value = "On";
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        var store = BindRequestCommands(f);
        var target = new ForegroundConversationTarget(store.Session, static () => true);
        f.ViewModel.PublishForegroundConversationTarget(target);
        f.ViewModel.BindSpokenSendReview(admitted => (admitted, static () => { }));
        var ready = ObserveSpokenSendCapture(f);
        var operation = f.RaiseActivatedTranscriptAsync("Kora, ask this conversation original", 1);
        await Task.WhenAny(operation, ready).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        ready.IsCompleted.Should().BeTrue(f.ViewModel.ResponseBody);
        f.Voice.StartedPhrases.Should().Equal("send", "cancel");
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? native = null;
        if (boundary is "call") { f.CallState.SetState(Kora.Core.Communication.CallState.Unknown); }
        if (boundary is "owner")
        {
            f.ViewModel.BindVoiceOwnershipGate(static () => false);
            f.Voice.RaiseTranscript("send", 1);
        }
        if (boundary is "target")
        { f.ViewModel.PublishForegroundConversationTarget(new(store.Session, static () => true)); }
        if (boundary is "native") { native = f.ViewModel.ConfirmForegroundRequestAsync(); }
        if (boundary is "settings-refresh") { f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service); }
        store.RequestRetentions.Should().Be(0);
        operation.IsCompleted.Should().BeFalse();
        f.Voice.StopGate.SetResult();
        if (native is not null) { await native.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); }
        await operation.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        store.RequestRetentions.Should().Be(boundary is "native" ? 1 : 0);
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpokenSendRoutingReadbackKeepsExactOriginalVoiceSelection(bool fresh)
    {
        var f = await RoutingReplyHostAsync();
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        setting.Store.Value = "On";
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        f.ViewModel.BindSessionRouting(async (body, origin, admitted) =>
        {
            var review = RoutingVoiceReview(body, origin);
            var selection = new Kora.Application.Hosting.SessionRouting.SessionRoutingSelection(review,
                fresh ? null : review.Candidates[0].Entry);
            await using var reply = f.ViewModel.CreateSessionRoutingSpokenSendReply(review, selection, admitted)!;
            var operation = reply.StartAsync();
            f.TextToSpeech.SpokenText.Should().Be("Send to " + (fresh ? "New conversation" : "Presented") + ": original?");
            f.Voice.RaiseTranscript("send", 1);
            (await operation)!.Choice.Should().Be(Kora.Application.Hosting.SessionRouting.SessionRoutingVoiceReply.SessionRoutingVoiceChoice.Submit);
            f.ViewModel.ReleaseSessionRoutingVoiceReply(reply);
            return null;
        });
        await f.RaiseActivatedTranscriptAsync("Kora, original", 1).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpokenSendTrustedWakeNameRetainsOnlyAfterExactReplyAndRelease()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await using var setting = new SpokenSendReplyConfigurationServiceTests.Fixture();
        setting.Store.Value = "On";
        f.ViewModel.BindSpokenSendReplyConfiguration(setting.Service);
        f.ViewModel.BindSessionRoutingCaptureRelease(HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        var store = BindRequestCommands(f);
        await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            store.Session.Authority.Generation, 0, new("Build"), static () => true, TestContext.Current.CancellationToken);
        store.ClaudePageRead = (_, _) => new([store.Session], null);
        f.ViewModel.BindSpokenSendReview(admitted => (admitted, static () => { }));
        var ready = ObserveSpokenSendCapture(f);
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("in Build, original", 1, first.Binding.Generation));
        try
        {
            await ready.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            f.Voice.StartedPhrases.Should().Equal("send", "cancel");
            store.RequestRetentions.Should().Be(0);
            f.Voice.RaiseTranscript("send", 1);
            await stage.NextAsync();
            store.RequestRetentions.Should().Be(1);
            store.RequestMessage!.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        }
        finally
        {
            await f.ViewModel.DisableListeningFromTrayAsync();
            await voice.StopAsync(TestContext.Current.CancellationToken);
            await voice.Completion;
        }
        await f.ViewModel.DisposeAsync();
    }

    private static Task ObserveSpokenSendCapture(Fixture f)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.SessionRequestVoiceReplyStatus), StringComparison.Ordinal)
                && f.ViewModel.SessionRequestVoiceReplyStatus.StartsWith("Speak exactly", StringComparison.Ordinal))
            {
                f.ViewModel.PropertyChanged -= Changed;
                ready.TrySetResult();
            }
        }
        f.ViewModel.PropertyChanged += Changed;
        return ready.Task;
    }
}
