using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Provider-neutral admission, not execution or an alternate provider selector.</summary>
public interface IModelTurnAdmission
{
    bool IsQuiescent { get; }
    Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
        ModelHandoffReview? review, CancellationToken token);
}
