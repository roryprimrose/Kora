using Kora.Application.Hosting;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Storage;

namespace Kora.Application.KnowledgeSources;

public sealed class SessionKnowledgeSourceWorkflow(SessionWorkspaceService sessions, KnowledgeSourceManager manager,
    IKnowledgeSourceStore sources, KnowledgeSourceAnswerWorkflow answers, TimeProvider time) : IAsyncDisposable
{
    public sealed record BindingReview(SessionWorkspaceEntry Target, KnowledgeSource Source,
        KnowledgeSourceReference Revision, HostRequest Original, DateTimeOffset ExpiresAt)
    {
        public const string Disclosure = "Associate only this exact already registered revision with this conversation. "
        + "No source bytes or index are copied. Source retention and session retention remain independent and unchanged. "
        + "Reopening reads association metadata only. Every source open needs fresh private access/integrity admission; "
        + "every question needs its own question/excerpt/target review and separate one-shot LocalOnly/toolsNONE confirmation. "
        + "Refresh, withdrawal, deletion or lost access makes this association unusable; no newer revision is substituted. "
        + "Detach removes only the association, not the source or retained conversation.";
    }

    public sealed record OpenedSource(SessionWorkspaceEntry Target, SessionKnowledgeSourceBinding Binding,
        KnowledgeSource Source, KnowledgeSourceRevision Revision);

    private KnowledgeSourceAnswerUse? bindingUse;
    private BindingReview? review;
    private OpenedSource? opened;
    private bool disposed;

    public Task<SessionKnowledgeSourceBinding?> ReadBindingAsync(SessionWorkspaceEntry target, CancellationToken token) =>
        sessions.ReadKnowledgeSourceBindingAsync(target.Authority.SessionId, token);

    public Task<IReadOnlyList<KnowledgeSource>> ListSourcesAsync(CancellationToken token) => sources.ListAsync(token);

    public async Task<BindingReview> ReviewBindingAsync(SessionWorkspaceEntry target,
        KnowledgeSource source, Func<bool> eligible, CancellationToken token)
    {
        RequireInput(eligible, token);
        await RevokeAsync().ConfigureAwait(false);
        await ValidateTargetAsync(target, null, eligible, token).ConfigureAwait(false);
        bindingUse = await manager.OpenAnswerUseAsync(source, eligible, token).ConfigureAwait(false);
        try
        {
            await ValidateTargetAsync(target, null, eligible, token).ConfigureAwait(false);
            await bindingUse.ValidateAsync(token).ConfigureAwait(false);
            review = new(target, source, bindingUse.Sources[0].Revision.Reference,
                HostActivity.RequireCurrent().Request, time.GetUtcNow().AddMinutes(2));
            return review;
        }
        catch { await RevokeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<SessionKnowledgeSourceBinding> ConfirmBindingAsync(BindingReview exact,
        Func<bool> eligible, CancellationToken token)
    {
        RequireInput(eligible, token);
        if (!ReferenceEquals(review, exact) || bindingUse is not { } use
            || time.GetUtcNow() >= exact.ExpiresAt || time.GetUtcNow() < exact.ExpiresAt.AddMinutes(-2)
            || HostActivity.RequireCurrent().Request.RequestId == exact.Original.RequestId)
        { throw new InvalidOperationException("Separate confirmation of the exact current binding review is required."); }
        review = null;
        try
        {
            await ValidateTargetAsync(exact.Target, null, eligible, token).ConfigureAwait(false);
            await use.ValidateAsync(token).ConfigureAwait(false);
            var result = await sessions.BindKnowledgeSourceAsync(exact.Target, exact.Revision, exact.Source.Version,
                eligible, token).ConfigureAwait(false);
            await use.ValidateAsync(token).ConfigureAwait(false);
            await ValidateTargetAsync(exact.Target, result, eligible, token).ConfigureAwait(false);
            return result;
        }
        finally { await RevokeAsync().ConfigureAwait(false); }
    }

    public async Task<OpenedSource> OpenSourceAsync(SessionWorkspaceEntry target,
        SessionKnowledgeSourceBinding binding, Func<bool> eligible, CancellationToken token)
    {
        RequireInput(eligible, token);
        await RevokeAsync().ConfigureAwait(false);
        await ValidateTargetAsync(target, binding, eligible, token).ConfigureAwait(false);
        var source = (await sources.ListAsync(token).ConfigureAwait(false)).SingleOrDefault(item =>
            item.Id == binding.Source.SourceId && item.Version == binding.SourceVersion && item.State == KnowledgeSourceState.Enabled)
            ?? throw new InvalidOperationException("The associated exact enabled revision is unavailable. No replacement was selected.");
        bindingUse = await manager.OpenAnswerUseAsync(source, eligible, token).ConfigureAwait(false);
        try
        {
            var revision = bindingUse.Sources[0].Revision;
            if (!binding.Matches(target, source, revision.Reference))
            { throw new InvalidOperationException("The associated revision is retired. Detach explicitly before reviewing another."); }
            await ValidateTargetAsync(target, binding, eligible, token).ConfigureAwait(false);
            opened = new(target, binding, source, revision);
            return opened;
        }
        catch { await RevokeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<KnowledgeSourceAnswerPreparation> PrepareQuestionAsync(OpenedSource exact,
        string question, Func<bool> eligible, CancellationToken token)
    {
        RequireInput(eligible, token);
        if (!ReferenceEquals(opened, exact))
        { throw new InvalidOperationException("Explicitly open the exact associated revision before reviewing a question."); }
        bool Current() => !disposed && eligible() && ReferenceEquals(opened, exact);
        Task Validate(CancellationToken cancellation) =>
            ValidateTargetAsync(exact.Target, exact.Binding, Current, cancellation);
        await Validate(token).ConfigureAwait(false);
        bindingUse = null;
        return await answers.PrepareForSessionAsync(exact.Source, exact.Binding.Source, exact.Target,
            question, Current, Validate, token).ConfigureAwait(false);
    }

    public Task<LocalFileAnswerResult> AnswerAsync(KnowledgeSourceAnswerReview exact, Func<bool> eligible,
        CancellationToken token)
    {
        RequireInput(eligible, token);
        return answers.AnswerAsync(exact, eligible, token);
    }

    public async Task DetachAsync(SessionWorkspaceEntry target, SessionKnowledgeSourceBinding binding,
        Func<bool> eligible, CancellationToken token)
    {
        RequireInput(eligible, token);
        await RevokeAsync().ConfigureAwait(false);
        await ValidateTargetAsync(target, binding, eligible, token).ConfigureAwait(false);
        await sessions.DetachKnowledgeSourceAsync(binding, eligible, token).ConfigureAwait(false);
    }

    private async Task ValidateTargetAsync(SessionWorkspaceEntry target, SessionKnowledgeSourceBinding? binding,
        Func<bool> eligible, CancellationToken token)
    {
        RequireInput(eligible, token);
        var current = await sessions.ReadKnowledgeSourceTargetAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        var actual = await sessions.ReadKnowledgeSourceBindingAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        RequireInput(eligible, token);
        if (!current.Authority.IsActive || current != target || actual != binding)
        { throw new InvalidOperationException("The reviewed conversation or exact source association changed."); }
    }

    private void RequireInput(Func<bool> eligible, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        var host = HostActivity.RequireCurrent();
        if (!eligible() || host.Outcome != HostOperationOutcome.Unknown
            || host.Request.Origin != RequestOrigin.LocalUi || host.Request.InvocationId is not null)
        { throw new InvalidOperationException("Fresh original-user private native source admission is required."); }
    }

    public async Task RevokeAsync()
    {
        review = null;
        opened = null;
        bindingUse = null;
        await manager.RevokeAnswerUsesAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await RevokeAsync().ConfigureAwait(false);
        disposed = true;
        manager.Dispose();
    }
}
