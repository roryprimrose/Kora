using System.Collections.Immutable;

using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Tools.Sessions;

namespace Kora.Application.Tools;

internal sealed class SessionDiscoveryInvocation : IDisposable
{
    private readonly SessionWorkspaceService workspace;
    private readonly Func<bool> live;
    private readonly SessionsList action = new();
    private ImmutableArray<SessionWorkspaceEntry> source;
    private ImmutableArray<SessionDescriptor> records;
    private long changes;
    private volatile bool disposed;
    private bool confirmed;

    internal SessionDiscoveryInvocation(SessionWorkspaceService workspace, Func<bool> live)
    {
        this.workspace = workspace;
        this.live = live;
        workspace.SessionMetadataChanging += Invalidate;
        workspace.SessionLifecycleChanged += Invalidate;
        workspace.SessionRetired += Invalidate;
    }

    private void Invalidate(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> _) =>
        Interlocked.Increment(ref changes);

    internal async Task<SessionDiscoveryReview> PrepareAsync(string originalRequest,
        SessionListSearchCursor? cursor, int limit, CancellationToken token)
    {
        if (limit is < 1 or > SessionDiscoveryTool.MaximumRecords)
        { throw new ArgumentOutOfRangeException(nameof(limit)); }
        RequireCurrent(token);
        var page = await workspace.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.Active,
            string.Empty, cursor, limit, token).ConfigureAwait(false);
        RequireCurrent(token);
        source = page.Records;
        records = source.Select(entry => new SessionDescriptor(entry.Authority.SessionId.Value.ToString("D"),
            entry.Metadata?.Name.Value)).ToImmutableArray();
        if (action.Execute(records, new(0, SessionDiscoveryTool.MaximumRecords)).Outcome != CapabilityOutcome.Succeeded)
        { throw new InvalidDataException("The complete session metadata page exceeds the model result limit. Review a narrower source page."); }
        return new(originalRequest, records, page.Next);
    }

    internal async Task<bool> ConfirmAsync(CancellationToken token)
    {
        if (!await ValidateAsync(token).ConfigureAwait(false)) { return false; }
        confirmed = true;
        return true;
    }

    internal async ValueTask<bool> ValidateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current() || source.IsDefault) { return false; }
        foreach (var expected in source)
        {
            SessionListSearchPage page;
            try
            {
                page = await workspace.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.Active,
                    expected.Authority.SessionId.Value.ToString("D"), null, 1, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException) when (!Current())
            { return false; }
            token.ThrowIfCancellationRequested();
            if (!Current() || page.Records.Length != 1 || page.Records[0] != expected) { return false; }
        }
        return Current();
    }

    internal async ValueTask<CapabilityReply> InvokeAsync(string id, string input, CancellationToken token)
    {
        var parsed = CapabilityInputPolicy.Read(SessionDiscoveryTool.Descriptor, input);
        if (!confirmed || !string.Equals(id, SessionDiscoveryTool.Id, StringComparison.Ordinal)
            || parsed.DenialReason is not null || !await ValidateAsync(token).ConfigureAwait(false))
        { return new(CapabilityOutcome.Denied, parsed.DenialReason ?? "reviewed-session-source-not-current"); }
        token.ThrowIfCancellationRequested();
        return action.Execute(records, parsed.Page!);
    }

    private bool Current() => !disposed && Interlocked.Read(ref changes) == 0 && live();

    private void RequireCurrent(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current()) { throw new InvalidOperationException("The private original session-discovery request is no longer current."); }
    }

    public void Dispose()
    {
        disposed = true;
        workspace.SessionMetadataChanging -= Invalidate;
        workspace.SessionLifecycleChanged -= Invalidate;
        workspace.SessionRetired -= Invalidate;
    }
}
