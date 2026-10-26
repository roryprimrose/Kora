using Kora.Application.Skills;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Skills;
using Kora.Core.Network;
using Kora.Core.Dependencies;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SharedSkillInvocationController
{
    private bool toolMode;
    private SharedSkillSource? toolSource;
    private SharedSkillSnapshot? toolSnapshot;
    private ReviewedToolSkillReview? toolReview;
    private ReviewedToolSkillInvocation? toolInvocation;
    private LocalWebPageScope? toolScope;

    private async Task RunTool(string action, RequestOrigin origin, Func<bool> eligible)
    {
        var service = toolWorkflow ?? throw new InvalidOperationException("The reviewed one-tool workflow is not composed.");
        var opened = window ?? throw new InvalidOperationException("Review window is closed.");
        var token = lifetime!.Token;
        bool Current() => Eligible() && eligible() && ReferenceEquals(window, opened);
        if (!Current()) { throw new InvalidOperationException("Original review admission expired."); }
        if (action is "Targets" or "NextTargets")
        {
            var after = action is "NextTargets" ? next ?? throw new InvalidOperationException("No next page; refresh targets.") : (Guid?)null;
            RetireConfirmation();
            RetireToolReview();
            opened.Targets.ItemsSource = null;
            var page = await workflow.ReadTargetsAsync(after, Current, origin, token);
            if (!Current()) { return; }
            next = page.Next;
            opened.Targets.ItemsSource = page.Records.Where(item => item.Authority.IsActive)
                .Select(item => new SharedSkillConversationTarget(item)).ToArray();
            opened.Status.Text = "Select a target, then review the exact target and tool revision.";
            return;
        }
        if (action is "ReviewToolRevision")
        {
            RetireConfirmation();
            RetireToolReview();
            if (origin != requestOrigin)
            { throw new InvalidOperationException("Review must use the original request channel; enter a fresh request before changing channels."); }
            var target = (opened.Targets.SelectedItem as SharedSkillConversationTarget)?.Entry
                ?? throw new InvalidOperationException("Select exactly one existing Active conversation.");
            if (!Uri.TryCreate(opened.Address.Text, UriKind.Absolute, out var first)
                || !string.IsNullOrEmpty(opened.SecondAddress.Text)
                    && !Uri.TryCreate(opened.SecondAddress.Text, UriKind.Absolute, out _))
            { throw new InvalidOperationException("Enter one or two distinct absolute HTTP or HTTPS source URLs; model output cannot select them."); }
            var scope = new LocalWebPageScope(string.IsNullOrEmpty(opened.SecondAddress.Text)
                ? [first] : [first, new Uri(opened.SecondAddress.Text, UriKind.Absolute)]);
            ConversationText.Validate(opened.Request.Text ?? string.Empty, submission: true);
            var revision = toolInputRevision;
            toolReview = await service.ReviewAsync(toolSource!, toolSnapshot!, target,
                () => Current() && toolInputRevision == revision, requestOrigin, token);
            if (!Current()) { RetireToolReview(); return; }
            toolScope = scope;
            opened.Confirmation.Text = Describe(scope) + $"\nORIGINAL REQUEST\n{opened.Request.Text}";
            opened.Status.Text = "Exact source, target, inputs and finite web scope reviewed. Separately enable the local model context; no action permission is granted.";
            return;
        }
        var captured = toolReview ?? throw new InvalidOperationException("Review the exact target and tool revision first.");
        bool Reviewed() => Current() && captured.IsCurrent && ReferenceEquals(toolReview, captured);
        if (origin != captured.Origin || requestOrigin != captured.Origin)
        { RetireToolReview(); throw new InvalidOperationException("The original input channel changed; review again."); }
        if (action is "Enable")
        {
            await service.EnableAsync(captured, Reviewed, origin, token);
            if (Reviewed()) { opened.Status.Text = "One-tool revision enabled for one local-only use. Web access still requires its own per-action authorization."; }
        }
        else if (action is "Prepare")
        {
            RetireConfirmation();
            var revision = inputRevision;
            using var request = HostActivity.BeginRoot(HostRequest.Create(requestOrigin), HostActivityLayer.Desktop, HostOperation.Request);
            toolInvocation = await service.PrepareAsync(captured, opened.Request.Text ?? string.Empty,
                toolScope ?? throw new InvalidOperationException("Review the finite web scope first."),
                () => Reviewed() && inputRevision == revision, requestOrigin, token);
            request.Complete(HostOperationOutcome.Completed);
            if (!Reviewed() || inputRevision != revision) { RetireConfirmation(); return; }
            opened.Confirmation.Text = $"{opened.Instructions.Text}\n\nTARGET {captured.Target.Authority.SessionId.Value:D}\n"
                + $"ORIGIN {captured.Origin}\n{Describe(toolInvocation.Scope)}\nORIGINAL REQUEST\n{toolInvocation.Body}";
            opened.Confirm.IsEnabled = true;
            opened.Status.Text = "Confirm this exact revision, local model context, original request and existing target.";
        }
        else if (action is "Invoke")
        {
            var prepared = toolInvocation ?? throw new InvalidOperationException("Review this exact invocation before confirming.");
            toolInvocation = null;
            opened.Confirm.IsEnabled = false;
            var revision = inputRevision;
            using var request = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
            var receipt = await service.InvokeAsync(prepared, () => Reviewed() && inputRevision == revision,
                async (proposal, generation, address, admitted, approvalToken) =>
                {
                    if (questions is null) { throw new InvalidOperationException("Native per-action page approval is unavailable."); }
                    var spec = new QuestionSpec("Exact page approval", QuestionKind.SingleChoice, [new("once", "Approve once")]);
                    var presentation = await questions.AskConversationAsync(opened, proposal.Request, generation,
                        spec, proposal.ExpiresAt, admitted, approvalToken, proposal, address);
                    return new(presentation.Presented, presentation.Completed);
                }, token);
            request.Complete(HostOperationOutcome.Completed);
            if (Current())
            {
                opened.Confirmation.Text = string.Empty;
                opened.Status.Text = $"Retained exact request {receipt.Message.Request.RequestId.Value:D}: {receipt.Message.Outcome} / {receipt.Message.Reason}. "
                    + (receipt.Message.AssistantResponse?.Body ?? "No assistant answer. Inspect the exact conversation; no automatic replay.");
            }
        }
        else { throw new InvalidOperationException("Use the explicit tool skill review, target, request, enable, review invocation or confirm invocation commands."); }
    }

    private void RetireToolReview()
    {
        toolReview?.Dispose();
        toolReview = null;
        toolInvocation?.Dispose();
        toolInvocation = null;
        toolScope = null;
    }

    private static string Describe(LocalWebPageScope scope) =>
        $"At most {(scope.MaximumCalls == 1 ? "ONE" : "TWO")} host WebPageGet call(s); {scope.MaximumRounds} model rounds; "
        + $"cumulative {ModelToolProtocol.MaximumInputUtf8Bytes} input / {ModelToolProtocol.MaximumOutputUtf8Bytes} output UTF-8 bytes; "
        + $"{LocalWebPageTool.Descriptor.Limits.MaximumOutputUtf8Bytes} bytes per inert page result; "
        + $"{ModelToolProtocol.Deadline.TotalSeconds:0}-second deadline.\n"
        + string.Join("\n", scope.Destinations.Select((address, index) => $"SOURCE {index + 1}: {address.AbsoluteUri}"))
        + "\nEach source needs its own current once-only action approval. No scripts or hosted disclosure.";
}
