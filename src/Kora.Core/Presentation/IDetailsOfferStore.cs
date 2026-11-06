using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Presentation;

/// <summary>Only the exact newly completed source may admit a separate non-executing Details question.</summary>
public interface IDetailsOfferStore : Kora.Core.Storage.IHostInteractionStore
{
    /// <summary>Atomically admits the independent host control, fixed question and source-bound audit.</summary>
    ValueTask<HostQuestionRecord> AdmitDetailsOfferAsync(DetailsOfferBinding source, Func<bool> admitted, CancellationToken token);
    /// <summary>Seals only this presentation control; accepted Yes is not itself proof of reveal.</summary>
    ValueTask CompleteDetailsOfferAsync(HostQuestionKey key, bool revealed, Func<bool> admitted, CancellationToken token);
}