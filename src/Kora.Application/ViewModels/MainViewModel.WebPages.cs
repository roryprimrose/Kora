using Kora.Application.Diagnostics;
using Kora.Application.Network;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Network;
using Kora.Tools.Network;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly WebPageGet? webPageGet;
    private bool webPageRetrievalActive;

    public bool IsWebPageRetrievalActive
    {
        get => webPageRetrievalActive;
        private set => SetProperty(ref webPageRetrievalActive, value);
    }

    internal async Task ExecuteWebPageCommandAsync(
        WebPageCommand command,
        SecurityAuditInitiator initiator,
        CancellationToken cancellationToken = default)
    {
        if (command.Error is not null)
        {
            ShowFailure("Clarify the web-page address.", command.Error);
            return;
        }
        if (webPageGet is null)
        {
            ShowFailure("Web-page retrieval is unavailable.", "The host retrieval action is not composed.");
            return;
        }
        if (!IsHostInputEligible || IsBusy || IsWebPageRetrievalActive)
        {
            ShowInformation("Web-page retrieval is unavailable.",
                "Use the current owning unlocked host and wait for the active operation to finish.");
            return;
        }

        IsWebPageRetrievalActive = true;
        try
        {
            var address = command.Address!;
            var result = await webPageGet.ExecuteAsync(
                new(address),
                initiator,
                static (_, _) => ValueTask.FromResult(true),
                cancellationToken);
            if (!IsHostInputEligible)
            {
                throw new InvalidOperationException(
                    "Host admission changed before the retrieved page could be presented.");
            }
            if (result.Outcome == WebPageGetOutcome.Succeeded)
            {
                PresentResponse(
                    AssistantState.Information,
                    "Web page retrieved.",
                    $"{result.FinalAddress}{Environment.NewLine}{Environment.NewLine}{result.Text}"
                    + (result.Truncated ? Environment.NewLine + Environment.NewLine + "[Result truncated]" : string.Empty));
            }
            else
            {
                ShowFailure(
                    "Web page was not retrieved.",
                    $"{result.Reason}. Address: {result.FinalAddress ?? address}");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Retrieving an explicitly requested web page");
            ShowFailure("Web page was not retrieved.", exception.Message);
        }
        finally
        {
            IsWebPageRetrievalActive = false;
        }
    }
}
