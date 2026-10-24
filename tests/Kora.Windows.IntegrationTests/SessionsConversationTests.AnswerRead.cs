using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnswerReadNativeSelectionIsPassiveAndExactExplicitControlsShareOneWorkflow(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var playback = new NativeAnswerPlayback();
            await using var composed = new Composition(f, adapter: new AnswerAdapter(), answerPlayback: playback);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "Generate a retained answer";
            await model.SubmitConversationAsync();
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            var source = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            Window surface = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            surface.Show();
            try
            {
                surface.FindControl<Button>("ReadSelectedAnswer")!.IsEnabled.Should().BeFalse();
                surface.FindControl<ListBox>("AnswerSourceRecords")!.SelectedItem = source;
                model.SelectedAnswerRecord.Should().BeSameAs(source);
                playback.Calls.Should().Be(0, "selection cannot autoplay");
                surface.FindControl<Button>("ReadSelectedAnswer")!.IsEnabled.Should().BeTrue();
                Click(surface, "ReadSelectedAnswer");
                await (surface is CompactSessionWindow compactView ? compactView.ConversationOperation
                    : ((SessionsWindow)surface).ConversationOperation);
                playback.Calls.Should().Be(1);
                playback.Body.Should().Be(source.AssistantResponse!.Body);
                playback.Request!.SessionId.Should().Be(source.Request.SessionId);
                model.AnswerReadStatus.Should().Contain("fake completed once");
                var once = model.CaptureSelectedAnswerRead()!;
                using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                    HostActivityLayer.Desktop, HostOperation.Request);
                await once(f.Token);
                playback.Calls.Should().Be(2, "another deliberate request may read the same exact source");
                await once(f.Token);
                playback.Calls.Should().Be(2, "a captured input callback is consumed once");
                model.AnswerReadStatus.Should().Contain("refused");
                model.SelectedAnswerRecord = source with { Revision = source.Revision };
                model.SelectedAnswerRecord.Should().BeNull("equal records supplied by a caller are not observed native selections");
                await model.ReadSelectedAnswerAsync();
                playback.Calls.Should().Be(2);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("selection")]
    [InlineData("refresh")]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("channel")]
    public async Task AnswerReadNativeRetiresNoncooperativeLateCompletionWithoutReplay(string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var playback = new NativeAnswerPlayback { Release = release };
            await using var composed = new Composition(f, adapter: new AnswerAdapter(), answerPlayback: playback);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "Generate one answer";
            await model.SubmitConversationAsync();
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.SelectedAnswerRecord = model.ConversationMessages.Single();
            var channel = true;
            var once = model.CaptureSelectedAnswerRead(() => channel)!;
            using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Request);
            var pending = once(f.Token);
            await playback.Entered.Task;
            model.CanCancelAnswerRead.Should().BeTrue();
            await model.ReadSelectedAnswerAsync();
            playback.Calls.Should().Be(1);
            if (retirement is "cancel") { model.CancelAnswerRead(); }
            if (retirement is "selection") { model.SelectedAnswerRecord = null; }
            if (retirement is "refresh") { await model.ReadConversationAsync(); }
            if (retirement is "close") { model.Close(); }
            if (retirement is "privacy") { composed.Access.CanInspect = false; }
            if (retirement is "channel") { channel = false; }
            playback.Admitted!().Should().BeFalse();
            release.SetResult("late success must never publish");
            await pending;
            model.AnswerReadStatus.Should().NotContain("late success").And.Contain("cancelled");
            model.CanCancelAnswerRead.Should().BeFalse();
            await once(f.Token);
            playback.Calls.Should().Be(1);
        });
    }

    private sealed class NativeAnswerPlayback : ISessionAnswerPlayback
    {
        internal int Calls { get; private set; }
        internal string? Body { get; private set; }
        internal Kora.Core.Hosting.HostRequest? Request { get; private set; }
        internal Func<bool>? Admitted { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<string>? Release { get; init; }
        public async Task<string> ReadAnswerOnceAsync(ConversationMessage source, Func<bool> admitted,
            Func<CancellationToken, Task> validateSource, CancellationToken token)
        {
            Calls++;
            Request = HostActivity.RequireCurrent().Request;
            Admitted = admitted;
            await validateSource(token);
            Body = source.AssistantResponse!.Body;
            Entered.TrySetResult();
            return Release is null ? "fake completed once" : await Release.Task;
        }
    }
}
