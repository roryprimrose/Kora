using System.Text;
using Kora.Application.Hosting;
using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Application.Mcp;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.Skills;

public sealed partial class ReviewedToolSkillInvocationWorkflow
{
    public IReadOnlyList<Configuration.McpServerConfigurationSummary> ReadMcpServers() => conversation.ReadMcpServers();
    public Task<IReadOnlyList<McpAccountChoice>> ReadMcpAccountsAsync(CancellationToken token) =>
        conversation.ReadMcpAccountsAsync(token);

    internal McpResultFollowUpWorkflow CreateMcpResultFollowUpWorkflow() =>
        conversation.CreateMcpResultFollowUpWorkflow(Context.LocalSourceUsePolicy.Instance);

    internal long McpResultControlRevision => access.ControlRevision;

    internal Func<CancellationToken, Task> CaptureMcpResultSourceValidation(ReviewedToolSkillReview review)
    {
        var shared = (SharedToolSkillSource)review.Require(this, review.Origin);
        var snapshot = shared.Snapshot;
        var registration = shared.Registration;
        var consent = discovery.ConsentRevision;
        var useRevision = discovery.UseRevision;
        var control = access.ControlRevision;
        var expiresAt = review.ExpiresAt;
        return token => discovery.ValidateCapturedRevisionAsync(snapshot, registration,
            () => access.CanControl && access.CanInspect && access.ControlRevision == control
                && discovery.ConsentRevision == consent && discovery.UseRevision == useRevision
                && time.GetUtcNow() < expiresAt && discovery.IsUseAllowed(registration, snapshot),
            token);
    }

    internal Task ValidateMcpResultConfigurationAsync(string serverId, McpAccountReference account,
        string sourceRevision, CancellationToken token) =>
        conversation.ValidateMcpResultConfigurationAsync(serverId, account, sourceRevision, token);

    public async Task<ReviewedMcpSkillInvocation> PrepareMcpAsync(ReviewedToolSkillReview review, string originalRequest,
        string serverId, McpAccountReference account, string arguments, Func<bool> eligible,
        RequestOrigin origin, CancellationToken token)
    {
        try
        {
            if (!review.IsEnabled) { throw new InvalidOperationException("Separately enable the exact MCP skill revision first."); }
            await ValidateAsync(review, eligible, origin, token).ConfigureAwait(false);
            var reference = review.McpReference ?? throw new InvalidOperationException("The selected skill has no exact declarative MCP reference.");
            if (!string.Equals(reference.ServerId, serverId, StringComparison.Ordinal))
            { throw new InvalidOperationException("Independently select the exact included server declared by this skill; no name or endpoint substitution is admitted."); }
            ConversationText.Validate(originalRequest, submission: true);
            ConversationText.Validate(arguments, submission: true);
            using var parsed = ModelToolProtocol.ReadJson(Encoding.UTF8.GetBytes(arguments), McpToolSchema.MaximumArgumentsUtf8Bytes);
            if (parsed.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            { throw new InvalidDataException("Original MCP arguments must be one bounded JSON object."); }
            if (!string.Equals(arguments, parsed.RootElement.GetRawText(), StringComparison.Ordinal))
            { throw new InvalidDataException("Original MCP arguments must start and end with the object braces; outer JSON whitespace cannot be preserved by the model protocol."); }
            ArgumentNullException.ThrowIfNull(account);
            var source = (SharedToolSkillSource)review.Require(this, origin);
            var binding = new McpSkillBinding(reference, source.Snapshot.SourceQualifiedIdentity,
                source.Snapshot.RevisionDigest, originalRequest, arguments, source.Text, review.ExpiresAt);
            _ = SessionConversationService.CreateMcpSkillPreflightContext(
                new(new(Guid.NewGuid()), review.Target.Authority.SessionId, new(Guid.NewGuid()), origin),
                binding, time.GetUtcNow());
            return new(review, originalRequest, serverId, account, arguments);
        }
        catch { review.Dispose(); throw; }
    }

    public async Task<ConversationSubmissionReceipt> InvokeMcpAsync(ReviewedMcpSkillInvocation invocation,
        Func<bool> eligible, McpApprovalPresenter presenter, McpToolSelector selector, CancellationToken token,
        Action<HostRequest, McpModelResult>? onResult = null)
    {
        using (invocation)
        using (var review = invocation.Review)
        {
            var origin = HostActivity.Current?.Request.Origin ?? review.Origin;
            SessionConversationService.RequireOriginalSource(origin);
            invocation.Consume();
            review.Consume(this, origin);
            using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Target.Authority.SessionId,
                new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request, [review.Cause]);
            bool Current() => eligible() && review.IsCurrent;
            Task Validate(CancellationToken check) => ValidateAsync(review, Current, origin, check);
            try
            {
                await Validate(token).ConfigureAwait(false);
                var source = (SharedToolSkillSource)review.Require(this, origin);
                var binding = new McpSkillBinding(review.McpReference!, source.Snapshot.SourceQualifiedIdentity,
                    source.Snapshot.RevisionDigest, invocation.Body, invocation.Arguments, source.Text, review.ExpiresAt);
                var entry = Approval(source, HostActivity.RequireCurrent().Request, "skills.mcp-revision.invoke", SecurityAuditCategory.ProtectedOperation);
                audit.Write(entry);
                try
                {
                    var receipt = await conversation.SubmitReviewedMcpSkillAsync(review.Target, binding, invocation.ServerId,
                        invocation.Account, presenter, selector, Current, Validate, [review.Cause], origin, onResult, token).ConfigureAwait(false);
                    audit.Write(entry.WithOutcome(receipt.Message.Outcome == ModelTurnOutcome.Succeeded
                        ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Failed, "retained-terminal"));
                    activity.Complete(receipt.Message.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                        : receipt.Message.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
                    return receipt;
                }
                catch
                {
                    audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "invocation-unconfirmed"));
                    throw;
                }
            }
            catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
            catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        }
    }
}
