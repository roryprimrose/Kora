using System.Collections.Immutable;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Original-channel exact-name selection for passive remaining-work answers. Never routes execution.</summary>
public sealed class SessionRemainingWorkCommandService(
    SessionWorkspaceService workspace, SessionConversationService conversation, ISessionWorkspaceAccess access, TimeProvider time)
{
    private Pending? pending;
    private long epoch;
    internal SessionConversationService Conversation => conversation;

    public void Retire()
    {
        Interlocked.Exchange(ref pending, null);
        Interlocked.Increment(ref epoch);
    }

    public async Task<RemainingWorkCommandAnswer> ExecuteAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token, Func<bool>? choiceSource = null)
    {
        SessionConversationService.RequireOriginalSource(origin);
        if (!command.IsRemainingWork) { throw new ArgumentException("An exact remaining-work command is required.", nameof(command)); }
        var previous = Interlocked.Exchange(ref pending, null);
        var attempt = Interlocked.Increment(ref epoch);
        var control = access.ControlRevision;
        bool Live() => access.CanInspect && access.ControlRevision == control && admitted()
            && Interlocked.Read(ref epoch) == attempt;
        if (!Live()) { throw new InvalidOperationException("Original remaining-work input admission changed."); }
        if (command.Operation == SessionCommandOperation.RemainingWorkCancel)
        { return new(null, [], "Remaining-work choice cancelled. Nothing was resumed or changed."); }
        long? nameHead = null;
        SessionWorkspaceEntry target;
        if (command.Operation == SessionCommandOperation.RemainingWorkChoose)
        {
            if (previous is null || previous.Origin != origin || previous.Control != control || !previous.Admitted()
                || time.GetUtcNow() >= previous.ExpiresAt || command.RemainingChoice < 1 || command.RemainingChoice > previous.Choices.Length)
            { throw new InvalidOperationException("No unchanged original-channel remaining-work choice is current. Ask again."); }
            target = previous.Choices[command.RemainingChoice - 1];
        }
        else if (command.SessionId is { } id && command.RemainingTargetName is null)
        { target = await workspace.ReadExactMetadataAsync(new(id), token).ConfigureAwait(false); }
        else if (command.SessionId is null && command.RemainingTargetName is { } name)
        {
            var matches = await ReadNameChoicesAsync(name, Live, token).ConfigureAwait(false);
            if (matches.IsEmpty) { throw new InvalidOperationException("No exact readable-name conversation was found. Nothing was created or resumed."); }
            var before = await workspace.ReadWorkAsync(matches[0].Authority.SessionId, token).ConfigureAwait(false);
            var verified = await ReadNameChoicesAsync(name, Live, token).ConfigureAwait(false);
            var after = await workspace.ReadWorkAsync(matches[0].Authority.SessionId, token).ConfigureAwait(false);
            if (!Live() || before.AuthorityRevision != after.AuthorityRevision || !matches.SequenceEqual(verified))
            { throw new InvalidOperationException("Readable-name sources changed during resolution. Ask again; no focus or recency guess was made."); }
            nameHead = after.AuthorityRevision;
            if (matches.Length > 1)
            {
                var expires = time.GetUtcNow().AddSeconds(60);
                if (!Live()) { throw new InvalidOperationException("The original choice presentation was retired."); }
                pending = new(matches, origin, control, expires, choiceSource ?? admitted);
                return new(null, matches, "Several conversations have that exact name. Choose one numbered binding, or cancel. "
                    + "No focus/recency choice was made. This original-channel choice expires after 60 seconds.");
            }
            target = matches[0];
        }
        else { throw new ArgumentException("One explicit conversation target is required.", nameof(command)); }
        var answer = await conversation.ReadRemainingWorkAsync(target, Live, token).ConfigureAwait(false);
        if (!Live() || nameHead is { } expected && expected != answer.AuthorityRevision)
        { throw new InvalidOperationException("The original answer presentation or readable-name source changed."); }
        return new(answer, [], answer.Format());
    }

    private async Task<ImmutableArray<SessionWorkspaceEntry>> ReadNameChoicesAsync(SessionName name, Func<bool> live, CancellationToken token)
    {
        var matches = ImmutableArray.CreateBuilder<SessionWorkspaceEntry>();
        SessionListSearchCursor? cursor = null;
        for (var pageNumber = 0; ; pageNumber++)
        {
            var page = await workspace.SearchListAsync(SessionListSearchKind.ExactName, SessionListFilter.All,
                name.Value, cursor, 8, token).ConfigureAwait(false);
            if (!live()) { throw new InvalidOperationException("Original readable-name admission changed."); }
            matches.AddRange(page.Records);
            if (matches.Count > 8 || pageNumber == 15 && page.Next is not null)
            { throw new InvalidOperationException("The complete name match exceeds the bounded choice limit. Select one exact native conversation instead."); }
            if (page.Next is null) { return matches.ToImmutable(); }
            cursor = page.Next;
        }
    }

    private sealed record Pending(ImmutableArray<SessionWorkspaceEntry> Choices, RequestOrigin Origin,
        long Control, DateTimeOffset ExpiresAt, Func<bool> Admitted);
}