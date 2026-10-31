using System.Text;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.Skills;

/// <summary>Native-only execution of declared original host observations. No model or external dispatcher.</summary>
public sealed class ObservationSkillWorkflow(OwnedSkillInvocationWorkflow sources, OwnedSkillControlAdmission admission,
    ReadOnlyCapabilityRegistry registry, ISessionWorkspaceStore sessions, DependencyBootstrapper dependencies,
    ISecurityAuditLog audit, TimeProvider time, IOwnedDeclarativeSkillStore store, IArtifactUsePolicy usePolicy,
    ISessionProviderProfileStore profiles)
{
    public async Task<ObservationWorkflowUse> Review(string id, string version, SessionWorkspaceEntry target, string request,
        OwnedSkillAuthoringAuthority authority, Func<bool> eligible, CancellationToken token)
    {
        ConversationText.Validate(request, submission: true);
        var observed = dependencies.Observations;
        var policy = usePolicy.Revision;
        OwnedDeclarativeSkillDocument? document = null;
        bool Current() => eligible() && authority.IsCurrent() && ReferenceEquals(observed, dependencies.Observations)
            && policy == usePolicy.Revision && (document is null
                || usePolicy.IsRevisionAllowed(ArtifactUseIdentity.File(Path.Combine(store.Destination(document), "manifest.yaml")), document.RevisionDigest)
                    && usePolicy.IsRevisionAllowed(ArtifactUseIdentity.File(Path.Combine(store.Destination(document), "instructions.md")), document.RevisionDigest));
        var source = await sources.ReviewAsync(id, version, authority, Current, token).ConfigureAwait(false);
        ObservationWorkflowUse? use = null;
        try
        {
            document = source.Require(sources).Document;
            var definition = document.ObservationWorkflow
                ?? throw new InvalidDataException("Select an exact native observation-workflow package.");
            var profile = await profiles.ReadProviderProfile(target.Authority.SessionId, token).ConfigureAwait(false);
            use = new ObservationWorkflowUse(this, source, definition, target, request, authority, Current, profile);
            await Validate(use, token).ConfigureAwait(false);
            return use;
        }
        catch { use?.Dispose(); source.Dispose(); throw; }
    }

    public async Task Enable(ObservationWorkflowUse use, CancellationToken token)
    {
        RequireOwner(use);
        try
        {
            await Validate(use, token).ConfigureAwait(false);
            await sources.EnableAsync(use.Source, use.Authority, use.Current, token).ConfigureAwait(false);
            await Validate(use, token).ConfigureAwait(false);
            use.Transition(ObservationWorkflowState.Reviewed, ObservationWorkflowState.Enabled);
        }
        catch { use.Finish(ObservationWorkflowState.Failed); throw; }
    }

    public async Task<string> Invoke(ObservationWorkflowUse use,
        Func<ObservationWorkflowStep, Func<bool>, CancellationToken, Task<bool>> approve, CancellationToken token)
    {
        RequireOwner(use);
        use.Transition(ObservationWorkflowState.Enabled, ObservationWorkflowState.Running);
        using var budget = new CancellationTokenSource(ObservationWorkflowDefinition.MaximumDuration, time);
        using var joined = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token, use.Token);
        var admitted = joined.Token;
        try
        {
            await Validate(use, admitted).ConfigureAwait(false);
            use.Source.Consume(sources);
            var output = new StringBuilder("Source: Kora host observations. No model inference, probes or external actions.\n");
            foreach (var step in use.Definition.Steps)
            {
                await Validate(use, admitted).ConfigureAwait(false);
                var result = await admission.Run(use.Authority, use.Current, async (request, session, current, actionToken) =>
                {
                    if (!await approve(step, current, actionToken).ConfigureAwait(false))
                    { throw new OperationCanceledException("The exact observation was not approved; remaining steps were not run.", actionToken); }
                    await sources.ValidateAdmittedAsync(use.Source, use.Authority, current, actionToken).ConfigureAwait(false);
                    await RequireTarget(use, actionToken).ConfigureAwait(false);
                    await sources.ValidateAdmittedAsync(use.Source, use.Authority, current, actionToken).ConfigureAwait(false);
                    return await admission.Commit(request, session, current, () =>
                    {
                        var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ProtectedOperation,
                            "skills.observation-step.invoke", SecurityAuditOutcome.Requested,
                            use.Authority.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                            "definition.owned-skill");
                        audit.Write(entry);
                        try
                        {
                            actionToken.ThrowIfCancellationRequested();
                            if (!current() || !use.Source.IsCurrent)
                            { throw new InvalidOperationException("The independent observation action gate closed before dispatch."); }
                            var reply = registry.Invoke(registry.Admit(CapabilityLane.Native), step.Tool, step.Input, actionToken);
                            ModelToolProtocol.ValidateObservationResult(step.Tool, step.Input, reply);
                            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                            return reply;
                        }
                        catch
                        {
                            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "observation-unconfirmed"));
                            throw;
                        }
                    }, actionToken).ConfigureAwait(false);
                }, admitted).ConfigureAwait(false);
                await Validate(use, admitted).ConfigureAwait(false);
                output.Append("Step ").Append(step.Number).Append(" - ").Append(step.Tool).Append(" (schema 1)\n")
                    .Append(Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(result))).Append('\n');
                if (Encoding.UTF8.GetByteCount(output.ToString()) > ObservationWorkflowDefinition.MaximumResultBytes)
                { throw new InvalidDataException("The complete result budget was exceeded. No partial workflow result is published."); }
            }
            await Validate(use, admitted).ConfigureAwait(false);
            use.Transition(ObservationWorkflowState.Running, ObservationWorkflowState.Succeeded);
            use.Finish(ObservationWorkflowState.Succeeded);
            return output.ToString();
        }
        catch (OperationCanceledException) { use.Finish(ObservationWorkflowState.Cancelled); throw; }
        catch { use.Finish(ObservationWorkflowState.Failed); throw; }
    }

    private async Task Validate(ObservationWorkflowUse use, CancellationToken token)
    {
        RequireOwner(use);
        token.ThrowIfCancellationRequested();
        use.Token.ThrowIfCancellationRequested();
        if (!use.Current()) { throw new InvalidOperationException("Original observation workflow authority or dependency observations changed."); }
        await sources.ValidateAsync(use.Source, use.Authority, use.Current, token).ConfigureAwait(false);
        await RequireTarget(use, token).ConfigureAwait(false);
    }

    private async Task RequireTarget(ObservationWorkflowUse use, CancellationToken token)
    {
        if (!use.Target.Authority.IsActive
            || await sessions.ReadMetadataAsync(use.Target.Authority.SessionId, token).ConfigureAwait(false) != use.Target)
        { throw new InvalidOperationException("The reviewed Active target changed. Review again; no next step was dispatched."); }
        var profile = await profiles.ReadProviderProfile(use.Target.Authority.SessionId, token).ConfigureAwait(false);
        if (profile.Session != use.Target.Authority || profile.Held || profile.Revision != use.Profile.Revision
            || !string.Equals(profile.Desired.Serialize(), use.Profile.Desired.Serialize(), StringComparison.Ordinal))
        { throw new InvalidOperationException("The exact reviewed target profile is held or changed; no next step was dispatched."); }
        token.ThrowIfCancellationRequested();
        use.Token.ThrowIfCancellationRequested();
        if (!use.Current()) { throw new InvalidOperationException("Original observation workflow authority changed."); }
    }

    private void RequireOwner(ObservationWorkflowUse use)
    {
        if (!ReferenceEquals(use.Owner, this)) { throw new InvalidOperationException("A foreign workflow review cannot confer authority."); }
    }
}
