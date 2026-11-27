using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWakeNameRouteReviewsExactDestinationThenRetainsOriginalVoiceRequest(bool expanded)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new WakeNativeComposition();
            await f.Initialize(expanded);
            var native = f.Composition.Ui.Native;
            await using var conversation = new SessionConversationService(native.Interactions, native.Interactions,
                native.Sessions, native.Access, f.Composition.Turns, TimeProvider.System,
                NullLogger<SessionConversationService>.Instance, execution: f.Composition.Turns,
                cancellationAudit: native.Audit, interactions: native.Interactions);
            native.Main.BindSessionCommands(native.Sessions,
                new(native.Sessions, conversation, native.Access, TimeProvider.System));
            var selected = f.Composition.Ui.Model.SelectedSessionRecord;
            f.Composition.Ui.Model.ComposerBody = "independent unsent input";
            var fallbackWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((_, _) => fallbackWindows++);
            var session = await f.Stage.NextAsync();
            session.HoldClose = true;
            const string body = "  Original e\u0301\r\nbody -- data  ";
            session.Wake();
            await session.Capture.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            session.Capture.Transcript("in Alpha, " + body);
            session.Capture.Done.SetResult();
            await session.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                native.Main.ResponseTitle.Should().NotBe("Review conversation request.");
                f.Composition.Adapter.Calls.Should().Be(0);
            }
            finally { session.ReleaseClose.TrySetResult(); }
            await Task.WhenAny(f.Voice.Completion, f.Stage.NextAsync());
            if (f.Voice.Completion.IsCompleted) { await f.Voice.Completion; }
            native.Main.ResponseTitle.Should().Be("Review conversation request.");
            native.Main.ResponseBody.Should().Contain("Conversation: Alpha").And.Contain(body.Trim())
                .And.Contain("ActivatedVoice").And.Contain("60 seconds");
            session.Session.IsQuiescent.Should().BeTrue();
            f.Composition.Adapter.Calls.Should().Be(0);
            (await native.Interactions.ReadConversationPageAsync(f.Composition.Ui.First.Authority.SessionId,
                null, 50, native.Token)).Messages.Should().BeEmpty();
            native.Main.CommandText = "session request confirm";
            await native.Main.RunTypedCommand.ExecuteAsync();
            var message = (await native.Interactions.ReadConversationPageAsync(f.Composition.Ui.First.Authority.SessionId,
                null, 50, native.Token)).Messages.Should().ContainSingle().Which;
            message.Body.Should().Be(body);
            message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            message.Request.SessionId.Should().Be(f.Composition.Ui.First.Authority.SessionId);
            message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            f.Composition.Adapter.Calls.Should().Be(1);
            f.Composition.Audit.Events.Where(item => string.Equals(item.ActionId, "model.turn", StringComparison.Ordinal)).Select(item => item.Outcome)
                .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
                    SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            fallbackWindows.Should().Be(0);
            f.Composition.Ui.Model.SelectedSessionRecord.Should().Be(selected);
            f.Composition.Ui.Model.ComposerBody.Should().Be("independent unsent input");
            native.Main.CommandText = "session request confirm";
            await native.Main.RunTypedCommand.ExecuteAsync();
            f.Composition.Adapter.Calls.Should().Be(1);
        });
    }
}
