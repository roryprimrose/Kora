using System.Collections.Immutable;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Skills;

public sealed partial class OwnedSkillInvocationWorkflow
{
    /// <summary>Collects data only. Source enablement, complete preview and exact Use remain separate operations.</summary>
    public async Task<IReadOnlyDictionary<string, string>> CollectInputsAsync(
        OwnedSkillUseReview review, Kora.Core.Storage.SessionWorkspaceEntry target, string originalRequest,
        IReadOnlyDictionary<string, string> initial, OwnedSkillAuthoringAuthority authority, Func<bool> eligible,
        Func<OwnedSkillInputQuestions, QuestionSpec, CancellationToken, Task<HostInteractionDecision>> present,
        CancellationToken token)
    {
        try
        {
            RequireAction(authority, eligible, token);
            ConversationText.Validate(originalRequest, submission: true);
            var expected = review.Require(this);
            var template = expected.Document.InputTemplate
                ?? throw new InvalidOperationException("Only a reviewed owned v3 guidance skill declares literal input questions.");
            var values = initial.ToImmutableDictionary(StringComparer.Ordinal).ToBuilder();
            if (values.Keys.Any(name => !template.RequiredNames.Contains(name, StringComparer.Ordinal)))
            { throw new InvalidDataException("Input fields must match the exact reviewed specification."); }
            foreach (var value in values.Values.Where(value => !string.IsNullOrWhiteSpace(value)))
            { OwnedSkillInputQuestions.RequireValue(value); }
            await ValidateAsync(review, authority, eligible, token).ConfigureAwait(false);
            await RequireTargetAsync(target, eligible, token).ConfigureAwait(false);
            using var sourceContinuation = review.InputAuthority.Questions?.Open();
            using var requestContinuation = ReferenceEquals(review.InputAuthority, authority) ? null : authority.Questions?.Open();
            var remaining = TimeSpan.FromTicks(Math.Max(0, (review.ExpiresAt - time.GetUtcNow()).Ticks));
            using var expiry = new CancellationTokenSource(remaining, time);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
            return await admission.Run(authority, () => eligible() && review.IsCurrent, async (request, session, live, admitted) =>
            {
                async Task Validate(CancellationToken cancellation)
                {
                    Require(() => live() && review.IsCurrent && authority.IsCurrent(), cancellation);
                    var actual = await store.Read(expected.Document.Id, expected.Document.Version, cancellation).ConfigureAwait(false);
                    if (actual is null || !expected.IsSameRevision(actual))
                    { throw new InvalidDataException("The selected skill source changed. Start a fresh review."); }
                    await RequireTargetAsync(target, live, cancellation).ConfigureAwait(false);
                    var intent = await admission.ReadIntent(request, cancellation).ConfigureAwait(false);
                    if (intent is null || intent.Task.Request != request || intent.Task.IsTerminal
                        || intent.Generation != session.Generation)
                    { throw new InvalidOperationException("The original skill input collection intent changed."); }
                    Require(() => live() && review.IsCurrent && authority.IsCurrent(), cancellation);
                }
                var observation = await admission.ReadIntent(request, admitted).ConfigureAwait(false);
                if (observation is null || observation.Task.Request != request || observation.Task.IsTerminal
                    || observation.Generation != session.Generation)
                { throw new InvalidOperationException("The exact input collection intent is unavailable."); }
                foreach (var name in template.RequiredNames)
                {
                    if (values.TryGetValue(name, out var existing) && !string.IsNullOrWhiteSpace(existing)) { continue; }
                    await Validate(admitted).ConfigureAwait(false);
                    var spec = new QuestionSpec(
                        $"Supply the literal value for {name} in the selected skill {expected.Document.Name}. "
                        + "This answer is private data only; it cannot Save, Enable, select a target or confirm Use.",
                        QuestionKind.Text, [], maximumTextLength: Kora.Core.Artifacts.OwnedPromptTemplate.MaximumValueLength,
                        purpose: OwnedSkillInputQuestions.Purpose, sourceId: "owned-skill-input");
                    using var questions = new OwnedSkillInputQuestions(request, session, observation.Task, spec, review.ExpiresAt,
                        time, Validate, (commit, cancellation) => admission.Commit(request, session, live, commit, cancellation),
                        audit);
                    var decision = await present(questions, spec, admitted).ConfigureAwait(false);
                    await Validate(admitted).ConfigureAwait(false);
                    values[name] = questions.Accept(decision);
                }
                await Validate(admitted).ConfigureAwait(false);
                var complete = values.ToImmutable();
                var expanded = template.Expand(complete, admitted);
                ConversationText.RequireNoCredentials(expanded);
                return (IReadOnlyDictionary<string, string>)complete;
            }, lifetime.Token).ConfigureAwait(false);
        }
        catch { review.Dispose(); throw; }
    }
}
