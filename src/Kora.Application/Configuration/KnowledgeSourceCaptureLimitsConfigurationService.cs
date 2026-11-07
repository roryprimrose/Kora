using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class KnowledgeSourceCaptureLimitsConfigurationService(
    IKnowledgeSourceStore store, IKnowledgeSourceCaptureLimitsPreferences preferences, ISecurityAuditLog audit,
    TimeProvider time, ILogger<KnowledgeSourceCaptureLimitsConfigurationService> logger)
{
    private int busy;
    private KnowledgeSourceCaptureLimitsReview? current;

    public async Task<(KnowledgeSource Source, KnowledgeSourceCaptureLimits? Saved)> GetAsync(string name,
        Func<bool> eligible, CancellationToken token, KnowledgeSource? expected = null)
    {
        Enter(eligible, token);
        try
        {
            var source = await ResolveAsync(name, eligible, token).ConfigureAwait(false);
            if (expected is not null && (source with { Folder = expected.Folder } != expected || !source.Folder.Matches(expected.Folder)))
            { throw new InvalidOperationException("The selected source inventory changed; reload before configuration."); }
            var saved = preferences.Load(source.Id);
            Check(eligible, token);
            return (source, saved);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        { LimitsUnavailable(exception); throw; }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task<KnowledgeSourceCaptureLimitsReview> ReviewAsync(string name, KnowledgeSourceCaptureLimits? proposed,
        Func<bool> eligible, CancellationToken token, KnowledgeSource? expected = null)
    {
        var (source, saved) = await GetAsync(name, eligible, token, expected).ConfigureAwait(false);
        Check(eligible, token);
        current = new(source, saved, proposed, time.GetUtcNow().AddMinutes(2), eligible);
        return current;
    }

    public void Cancel()
    {
        if (Volatile.Read(ref busy) != 0) { throw new InvalidOperationException("Join the committing limits operation before cancellation."); }
        current = null;
    }

    public async Task ConfirmAsync(KnowledgeSourceCaptureLimitsReview review, Func<bool> eligible, CancellationToken token)
    {
        Enter(eligible, token);
        try
        {
            CheckReview();
            current = null;
            var source = await ResolveAsync(review.Source.Name, eligible, token).ConfigureAwait(false);
            if (source with { Folder = review.Source.Folder } != review.Source || !source.Folder.Matches(review.Source.Folder)
                || preferences.Load(source.Id) != review.Saved)
            { throw new InvalidOperationException("The exact source or saved limits changed; reload and review again."); }
            Check(review.Eligible, token);
            if (time.GetUtcNow() >= review.Expires) { throw new InvalidOperationException("Capture limits review expired."); }
            var request = HostActivity.RequireCurrent().Request;
            var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                "configuration.knowledge-capture-limits", SecurityAuditOutcome.Requested,
                request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                "knowledge.device-local");
            audit.Write(entry);
            var preferenceWriteStarted = false;
            try
            {
                var beforeWrite = await ResolveAsync(review.Source.Name, eligible, token).ConfigureAwait(false);
                if (beforeWrite with { Folder = review.Source.Folder } != review.Source
                    || !beforeWrite.Folder.Matches(review.Source.Folder) || preferences.Load(source.Id) != review.Saved)
                { throw new InvalidOperationException("Source or preference changed before the exact reviewed write."); }
                Check(eligible, token);
                Check(review.Eligible, token);
                if (time.GetUtcNow() >= review.Expires) { throw new InvalidOperationException("Capture limits review expired before write."); }
                preferenceWriteStarted = true;
                preferences.BeginWrite(source.Id);
                preferences.Save(source.Id, review.Proposed);
                RequireReadback();
                Check(eligible, token);
                Check(review.Eligible, token);
                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                preferences.ConfirmWrite(source.Id);
                try
                {
                    if (preferences.Load(source.Id) != review.Proposed)
                    { throw new InvalidDataException("Confirmed source limits do not match the reviewed preference."); }
                }
                catch { preferences.BeginWrite(source.Id); throw; }
            }
            catch (OperationCanceledException)
            {
                if (preferenceWriteStarted) { preferences.BeginWrite(source.Id); }
                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Cancelled));
                throw;
            }
            catch
            {
                if (preferenceWriteStarted) { preferences.BeginWrite(source.Id); }
                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed));
                throw;
            }

            void RequireReadback()
            {
                if (preferences.ReadBack(source.Id) != review.Proposed)
                { throw new InvalidDataException("Capture limits readback does not match; refresh remains unavailable."); }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        { LimitsUnavailable(exception); throw; }
        finally { Volatile.Write(ref busy, 0); }

        void CheckReview()
        {
            ArgumentNullException.ThrowIfNull(review);
            if (!ReferenceEquals(current, review) || time.GetUtcNow() >= review.Expires)
            { throw new InvalidOperationException("The exact capture limits review is stale or already consumed."); }
            Check(review.Eligible, token);
        }
    }

    private async Task<KnowledgeSource> ResolveAsync(string name, Func<bool> eligible, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))
        { throw new InvalidDataException("Use one exact registered source name."); }
        var sources = await store.ListAsync(token).ConfigureAwait(false);
        Check(eligible, token);
        var matches = sources.Where(source => string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].State == KnowledgeSourceState.Removing)
        { throw new InvalidOperationException("The named source is unknown, ambiguous or being removed."); }
        return matches[0];
    }

    private void Enter(Func<bool> eligible, CancellationToken token)
    {
        Check(eligible, token);
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        { throw new InvalidOperationException("A source limits operation is still owned."); }
    }

    private static void Check(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var host = HostActivity.RequireCurrent();
        if (!eligible() || host.Outcome != HostOperationOutcome.Unknown
            || host.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || host.Request.InvocationId is not null)
        { throw new InvalidOperationException("Source limits require original private user admission."); }
    }
}
