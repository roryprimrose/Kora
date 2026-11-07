using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.Application.KnowledgeSources;

public sealed partial class KnowledgeSourceManager
{
    private KnowledgeSourceAnswerUse? answerUse;

    internal async Task<KnowledgeSourceAnswerUse> OpenAnswerUseAsync(KnowledgeSource exact, Func<bool> eligible,
        CancellationToken token) =>
        await OpenAnswerUseAsync([exact], eligible, token).ConfigureAwait(false);

    internal async Task<KnowledgeSourceAnswerUse> OpenAnswerUseAsync(IReadOnlyList<KnowledgeSource> sources, Func<bool> eligible,
        CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var host = HostActivity.RequireCurrent();
        if (!eligible() || host.Outcome != HostOperationOutcome.Unknown || host.Request.InvocationId is not null
            || host.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("A fresh deliberate private question is required."); }
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        { throw new InvalidOperationException("A source operation must finish before answer review."); }
        try
        {
            ArgumentNullException.ThrowIfNull(sources);
            var selected = sources.Take(3).ToArray();
            if (selected.Length is < 1 or > 2 || selected.Any(item => item is null || item.State != KnowledgeSourceState.Enabled)
                || selected.Select(item => item.Id).Distinct().Count() != selected.Length)
            { throw new InvalidDataException("Explicitly select one or two distinct enabled sources."); }
            await RevokeAnswerUsesAsync().ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(KnowledgeSourcePolicy.MaximumOperationTime, time);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            token = operation.Token;
            return await Audited("answer.inspect", async () =>
            {
                var inputs = new List<(KnowledgeSourceAnswerSource Evidence, IKnowledgeSourceSelection Access)>();
                KnowledgeSourceAnswerUse? opened = null;
                try
                {
                    foreach (var exact in selected)
                    {
                        var access = await inspector.InspectSourceAsync(exact.Folder.CanonicalPath, exact.Folder.Scope,
                            exact.Folder.CaptureLimits, token)
                            .ConfigureAwait(false);
                        try
                        {
                            if (!access.Metadata.Matches(exact.Folder))
                            { throw new InvalidDataException("Original source access or membership changed; refresh explicitly."); }
                            var snapshot = await store.ReadAsync(exact, token).ConfigureAwait(false);
                            inputs.Add((new(exact, snapshot), access));
                        }
                        catch { access.Dispose(); throw; }
                    }
                    opened = new(inputs.AsReadOnly(), store, eligible, time);
                    await opened.ValidateAsync(token).ConfigureAwait(false);
                    answerUse = opened;
                    return opened;
                }
                catch
                {
                    if (opened is null) { foreach (var input in inputs) { input.Access.Dispose(); } }
                    else { await opened.DisposeAsync().ConfigureAwait(false); }
                    throw;
                }
            }).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task RevokeAnswerUsesAsync()
    {
        if (answerUse is not { } use) { return; }
        await use.DisposeAsync().ConfigureAwait(false);
        Interlocked.CompareExchange(ref answerUse, null, use);
    }

    internal async Task ReleaseAnswerUseAsync(KnowledgeSourceAnswerUse exact)
    {
        await exact.DisposeAsync().ConfigureAwait(false);
        Interlocked.CompareExchange(ref answerUse, null, exact);
    }
}
