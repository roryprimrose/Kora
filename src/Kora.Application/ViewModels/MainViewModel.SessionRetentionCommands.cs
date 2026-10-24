using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private RetentionCommandBinding? sessionRetentionCommands;

    private sealed record RetentionCommandBinding(SessionRetentionCommandService Service,
        Func<SessionRetentionReview, Func<bool>, CancellationToken, Task> Open);

    public void BindSessionRetentionCommands(SessionRetentionCommandService service,
        Func<SessionRetentionReview, Func<bool>, CancellationToken, Task> openNativeReview)
    {
        sessionRetentionCommands?.Service.Retire();
        sessionRetentionCommands = new(service, openNativeReview);
    }

    private Task<SessionCommandResult> ExecuteSessionRetentionCommandAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        var binding = sessionRetentionCommands
            ?? throw new InvalidOperationException("The exact retention command service and native review surface are unavailable.");
        return binding.Service.ExecuteAsync(command, origin, admitted, binding.Open, token);
    }

    private void RetireSessionCommandReviews()
    {
        sessionRequests?.Retire();
        sessionRetentionCommands?.Service.Retire();
        conversationSearchCommands?.Retire();
    }
}
