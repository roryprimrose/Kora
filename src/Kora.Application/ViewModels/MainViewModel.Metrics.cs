using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ExecuteMetricsCommandAsync(SessionCommand command, SecurityAuditInitiator initiator,
        Func<bool>? original, bool activated, ForegroundConversationTarget? foreground)
    {
        var origin = OriginalOrigin(initiator);
        if (!CanAnswerRemainingWork || command.Operation == SessionCommandOperation.Invalid
            || origin is not (Kora.Core.Hosting.RequestOrigin.LocalUi or Kora.Core.Hosting.RequestOrigin.ActivatedVoice)
            || origin == Kora.Core.Hosting.RequestOrigin.ActivatedVoice && !activated)
        {
            Transcript = "Session metrics unavailable. " + SessionCommand.MetricsSyntax;
            return;
        }
        var captured = CaptureRemainingWorkEligibility(origin, original ?? (static () => true));
        bool Live() => captured() && (command.SessionId is not null || foreground?.IsCurrent == true);
        await HostRequestRunner.RunAsync(origin, async () =>
        {
            try
            {
                var workspace = sessionCommands ?? throw new InvalidOperationException("The session workspace is unavailable.");
                var conversation = remainingWorkConversation ?? throw new InvalidOperationException("The session metrics owner is unavailable.");
                var target = command.SessionId is { } id
                    ? await workspace.ReadExactMetadataAsync(new(id), CancellationToken.None)
                    : foreground?.Entry ?? throw new InvalidOperationException("Select and focus one conversation first, or use an exact session ID.");
                var snapshot = await conversation.ReadMetricsAsync(target, Live, CancellationToken.None);
                if (!Live()) { throw new InvalidOperationException("The original metrics channel or foreground conversation changed."); }
                PresentResponse(AssistantState.Information, "Session model metrics.", snapshot.Format(), sourceAssigned: () =>
                {
                    forceVisualResponse = true;
                    sessionPresentationSources.Add(target.Authority.SessionId.Value);
                });
                var response = Interlocked.Read(ref webResponseRevision);
                bool Current() => Live() && response == Interlocked.Read(ref webResponseRevision);
                if (Current() && IsSpeechResponseEnabled)
                {
                    await TrySpeakCurrentResponseAsync(snapshot.FormatSpeech(), additionalEligibility: Current,
                        boundedExactReadback: true, cancellationToken: CancellationToken.None,
                        validateReadSource: async token => { await conversation.ReadMetricsAsync(target, Current, token); });
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException
                or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
            {
                ApplicationLog.Error(logger, exception, "Reading session metrics");
                if (captured()) { ShowFailure("Session metrics unavailable/changed.", exception.Message); }
            }
        });
    }
}
