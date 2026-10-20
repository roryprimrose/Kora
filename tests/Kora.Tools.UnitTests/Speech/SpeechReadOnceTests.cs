using AwesomeAssertions;
using Kora.Core.Voice;
using Kora.Tools.Speech;
using Kora.Core.Auditing;

namespace Kora.Tools.UnitTests.Speech;

public sealed class SpeechReadOnceTests
{
    [Fact]
    public async Task ExecuteForwardsOnlyTheExactTargetAndCancellationToHostAdmission()
    {
        var host = new Playback();
        var action = new SpeechReadOnce(host);
        var target = new ResponseSpeechTarget(Guid.NewGuid(), 41, 73);
        using var cancellation = new CancellationTokenSource();
        await action.ExecuteAsync(target, SecurityAuditInitiator.TypedCommand, cancellation.Token);
        host.Target.Should().Be(target);
        host.Initiator.Should().Be(SecurityAuditInitiator.TypedCommand);
        host.Token.Should().Be(cancellation.Token);
        SpeechReadOnce.ModelLaneUnavailableReason.Should().Be("current-response-model-tool-loop-not-qualified");
    }

    [Fact]
    public async Task ExecuteDoesNotSwallowCancellationOrRetryTheHost()
    {
        var host = new Playback();
        var action = new SpeechReadOnce(host);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var run = () => action.ExecuteAsync(new(Guid.NewGuid(), 1, 1), SecurityAuditInitiator.LocalUser, cancellation.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        host.Calls.Should().Be(1);
    }

    private sealed class Playback : ISpeechReadOncePlayback
    {
        public ResponseSpeechTarget Target { get; private set; }
        public SecurityAuditInitiator Initiator { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }

        public Task ReadOnceAsync(ResponseSpeechTarget target, SecurityAuditInitiator initiator, CancellationToken cancellationToken)
        {
            Calls++;
            Target = target;
            Initiator = initiator;
            Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
