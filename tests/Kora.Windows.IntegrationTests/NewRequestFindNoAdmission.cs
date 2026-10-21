using Kora.Application.Dependencies;
using Kora.Core.Dependencies;

namespace Kora.Windows.IntegrationTests;

internal sealed class NewRequestFindNoAdmission : IModelTurnAdmission
{
    public bool IsQuiescent => true;
    public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
        ModelHandoffReview? review, CancellationToken token) => throw new InvalidOperationException("Find never admits a model turn.");
}
