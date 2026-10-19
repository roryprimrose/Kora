using Kora.Core.Context;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed class LocalFileAnswerModel(LocalContextTurnWorkflow workflow) : ILocalFileAnswerModel
{
    public Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, Func<bool> isCurrent,
        CancellationToken cancellationToken) =>
        workflow.RunAsync(LocalContextSessionTarget.ReviewedFresh(context.Request), context, isCurrent, cancellationToken);
}
