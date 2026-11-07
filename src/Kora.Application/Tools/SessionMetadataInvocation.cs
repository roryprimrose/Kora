using System.Text.Json;

using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Tools.Sessions;

namespace Kora.Application.Tools;

internal sealed class SessionMetadataInvocation : IDisposable
{
    private readonly SessionWorkspaceService workspace;
    private readonly Func<bool> live;
    private readonly HostId<SessionIdentity> subject;
    private SessionWorkOverviewRow? source;
    private SessionMetadataSnapshot? snapshot;
    private bool confirmed;
    private bool served;
    private volatile bool retired;
    internal bool Served => served;

    internal SessionMetadataInvocation(SessionWorkspaceService workspace, HostId<SessionIdentity> subject, Func<bool> live)
    {
        this.workspace = workspace;
        this.subject = subject;
        this.live = live;
        workspace.SessionMetadataChanging += Invalidate;
        workspace.SessionLifecycleChanged += Invalidate;
        workspace.SessionRetired += Invalidate;
        workspace.WorkOverviewInvalidated += Invalidate;
    }

    private void Invalidate(HostId<SessionIdentity> changed)
    {
        if (changed == subject) { retired = true; }
    }

    internal async Task<SessionMetadataReview> PrepareAsync(string question, CancellationToken token)
    {
        RequireCurrent(token);
        if (source is not null) { throw new InvalidOperationException("Metadata preparation is single-use."); }
        source = await workspace.ReadSessionMetadataAsync(subject, token).ConfigureAwait(false);
        RequireCurrent(token);
        snapshot = SessionMetadataSnapshot.From(source);
        return new(question, snapshot);
    }

    internal async Task<bool> ConfirmAsync(CancellationToken token)
    {
        if (confirmed || !await ValidateAsync(token).ConfigureAwait(false)) { return false; }
        confirmed = true;
        return true;
    }

    internal async ValueTask<bool> ValidateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current || source is null) { return false; }
        var actual = await workspace.ReadSessionMetadataAsync(subject, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Current || actual.Session != source.Session || actual.Availability != source.Availability
            || (actual.Work is null) != (source.Work is null)) { return false; }
        // Observation time advances during a passive reread; all source revisions, records and omissions must remain exact.
        return actual.Work is null || string.Equals(JsonSerializer.Serialize(actual.Work with { ObservedAt = source.Work!.ObservedAt }),
            JsonSerializer.Serialize(source.Work), StringComparison.Ordinal);
    }

    internal async ValueTask<CapabilityReply> InvokeAsync(string id, string input, CancellationToken token)
    {
        if (!confirmed || served || !string.Equals(id, SessionMetadataTool.Id, StringComparison.Ordinal)
            || !await ValidateAsync(token).ConfigureAwait(false))
        { return new(CapabilityOutcome.Denied, "reviewed-exact-metadata-not-current"); }
        var result = new SessionsGet().Execute(snapshot!, input);
        if (result.Outcome == CapabilityOutcome.Succeeded) { served = true; }
        return result;
    }

    private bool Current => !retired && live();
    private void RequireCurrent(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current) { throw new InvalidOperationException("The original private metadata request has retired."); }
    }

    public void Dispose()
    {
        retired = true;
        workspace.SessionMetadataChanging -= Invalidate;
        workspace.SessionLifecycleChanged -= Invalidate;
        workspace.SessionRetired -= Invalidate;
        workspace.WorkOverviewInvalidated -= Invalidate;
        source = null;
        snapshot = null;
    }
}
