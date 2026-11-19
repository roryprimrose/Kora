using Kora.Application.Definitions;
using Kora.Core.Customizations;
using Kora.Core.Hosting;

namespace Kora;

internal sealed class CopilotCustomizationController(CopilotCustomizationWorkflow workflow)
{
    internal async Task RunAsync(CustomizationSource source, string target, string body, CustomizationView view,
        Func<bool> current, CancellationToken token)
    {
        var review = await workflow.ReviewAsync(source, target, body, RequestOrigin.LocalUi,
            async (disclosure, cancellation) => await view.Review("Review exact selected source read", disclosure,
                cancellation, "Read this exact source", "Cancel") is "Read this exact source", current, token);
        Require();
        if (await view.Review("Review hosted source disclosure", review.Text, token,
            "Authorize this exact hosted copy", "Cancel") is not "Authorize this exact hosted copy") { return; }
        Require();
        await workflow.ApproveHostedSourceAsync(review.Id, current, token);
        Require();
        if (await view.Review("Confirm exact conversation retention", review.Text, token,
            "Retain and submit this one request", "Cancel") is not "Retain and submit this one request") { return; }
        Require();
        var receipt = await workflow.ConfirmAsync(review.Id, current, token);
        Require();
        view.Status.Text = "Original USER request retained in the reviewed Active conversation. HOST "
            + receipt.Message.Outcome + " / " + receipt.Message.Reason + "; task " + receipt.Task.State
            + ". No automatic retry or continuing customization.";
        await view.Review("Selected Copilot request receipt", view.Status.Text
            + (receipt.Message.AssistantResponse is { } answer ? "\n\nGenuine ASSISTANT answer\n" + answer.Body : "\nNo assistant answer retained."),
            token, "Close");

        void Require()
        {
            token.ThrowIfCancellationRequested();
            if (!current()) { throw new OperationCanceledException("Selected Copilot presentation or original request changed."); }
        }
    }
}
