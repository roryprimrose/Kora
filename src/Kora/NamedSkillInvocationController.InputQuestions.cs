using Kora.Application.Skills;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class NamedSkillInvocationController
{
    private CancellationTokenSource? inputQuestions;

    private async Task CollectMissingInputs(NamedSkillInvocationWindow opened, OwnedSkillUseReview source,
        SessionWorkspaceEntry target, CancellationToken token)
    {
        var host = questions ?? throw new InvalidOperationException("The private skill input question host is unavailable. Enter fields manually.");
        var revision = inputRevision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        inputQuestions = cancellation;
        bool Exact() => Current() && revision == inputRevision && !cancellation.IsCancellationRequested;
        IReadOnlyDictionary<string, string> values;
        try
        {
            values = await owned.CollectInputsAsync(source, target, opened.Request.Text ?? "",
                opened.LiteralInputs.Capture(), original!, Exact,
                (collection, spec, admitted) => host.AskSkillInputAsync(opened, collection, spec, Exact, admitted),
                cancellation.Token);
            if (!Exact()) { throw new OperationCanceledException("The original named skill input fields changed.", cancellation.Token); }
        }
        finally { inputQuestions = null; }
        foreach (var pair in values) { opened.LiteralInputs.Inputs[pair.Key].Text = pair.Value; }
    }
}
