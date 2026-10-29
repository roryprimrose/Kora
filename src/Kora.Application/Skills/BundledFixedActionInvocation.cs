using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Skills;
using Kora.Tools.SessionControl;

namespace Kora.Application.Skills;

/// <summary>One host-resolved immutable package, one original-user invocation and at most one fixed effect.</summary>
public sealed class BundledFixedActionInvocation(
    BundledFixedScript script, IBundledScriptWorker worker, FixedSessionActionBroker broker,
    HostAuthorizationService authorization, BundledActionApprovalPresenter present,
    Func<BundledFixedScript> resolveCurrent, HostRequest request, HostRevision generation,
    Func<bool> admitted, Func<CancellationToken, Task<ApplicationLifecycleShutdown?>> prepare,
    TimeProvider time)
{
    private int used;
    public bool NativeRequestStarted { get; private set; }

    public async Task<FixedSessionEffectResult> ExecuteAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref used, 1) != 0 || request.InvocationId is null
            || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || HostActivity.RequireCurrent().Request != request)
        { throw new InvalidOperationException("The exact bundled invocation is missing or has already been used."); }
        var implementation = worker.QualifiedImplementationDigest
            ?? throw new InvalidOperationException("Bundled PowerShell execution has no qualified contained runtime.");
        var identity = broker.CurrentIdentityDigest
            ?? throw new InvalidOperationException("The current interactive Windows identity is unavailable.");
        var binding = BundledActionBinding.Create(script, implementation, identity);
        var proposal = new HostOperationProposal(request, new(Guid.NewGuid()), new(1), binding,
            HostOperationEffect.BoundedWrite, time.GetUtcNow().AddMinutes(2));
        bool Current()
        {
            if (token.IsCancellationRequested || !admitted() || time.GetUtcNow() >= proposal.ExpiresAt
                || !string.Equals(worker.QualifiedImplementationDigest, implementation, StringComparison.Ordinal)
                || !string.Equals(broker.CurrentIdentityDigest, identity, StringComparison.Ordinal))
            { return false; }
            var source = resolveCurrent();
            return source.Action == script.Action
                && BundledActionBinding.Create(source, implementation, identity) == binding;
        }
        if (!Current()) { throw new InvalidOperationException("The exact bundled package or source authority changed."); }
        var approved = await present(proposal, generation, Current, token).ConfigureAwait(false);
        if (approved is not { Outcome: HostInteractionOutcome.Consumed, Admission: { } admission }
            || admission.Proposal != proposal || admission.Dispatched || admission.GrantId is null || !Current())
        { throw new InvalidOperationException("The exact bundled script and effect were not granted by the original user."); }
        if (!await Authorized(admission, token).ConfigureAwait(false))
        { throw new InvalidOperationException("The exact bundled script grant is no longer current."); }
        var receipt = await worker.RunAsync(script, request, Current, token).ConfigureAwait(false);
        if (!Current() || receipt.Invocation != request || receipt.Action != script.Action
            || !string.Equals(receipt.DeclaredResourceDigest, script.Package.DeclaredResourceDigest, StringComparison.Ordinal)
            || !receipt.ScriptsCompleted || !receipt.ResourcesJoined || !worker.IsQuiescent)
        { throw new InvalidOperationException("The exact scripts or their physical resource join are unconfirmed. No native effect was requested."); }
        if (!await Authorized(admission, token).ConfigureAwait(false))
        { throw new InvalidOperationException("The bundled grant retired while the script worker was running."); }
        using var prepared = await prepare(token).ConfigureAwait(false);
        if (prepared is null || !prepared.IsCurrent || !Current())
        { throw new InvalidOperationException("The separate native confirmation or affected-work review was not accepted."); }
        await prepared.StopAndJoinAsync().ConfigureAwait(false);
        if (!prepared.IsCurrent || !await Authorized(admission, token).ConfigureAwait(false))
        { throw new InvalidOperationException("Required work did not join under current source authority. No native effect was requested."); }
        var dispatched = await authorization.DispatchAsync(request, admission, token).ConfigureAwait(false);
        if (dispatched is not { Outcome: HostInteractionOutcome.Consumed, Admission: { Dispatched: true } dispatch })
        { throw new InvalidOperationException("The one-use native effect admission could not be committed."); }
        ValueTask<bool> Authorize(CancellationToken checkToken) => Authorized(dispatch, checkToken);
        bool EffectCurrent() => prepared.IsCurrent && worker.IsQuiescent && Current();
        NativeRequestStarted = true;
        return await broker.ExecuteAsync(script.Action, identity, Authorize, EffectCurrent, token).ConfigureAwait(false);

        async ValueTask<bool> Authorized(SessionPermissionAdmission permission, CancellationToken checkToken)
        {
            if (!Current()) { return false; }
            var decision = await authorization.ValidateAdmissionAsync(request, permission, checkToken).ConfigureAwait(false);
            return decision.Outcome == HostInteractionOutcome.Approved && Current();
        }
    }
}