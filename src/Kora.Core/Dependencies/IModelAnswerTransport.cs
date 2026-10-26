namespace Kora.Core.Dependencies;

/// <summary>Optional local tool-free answer presentation; complete terminal validation remains mandatory.</summary>
public interface IModelAnswerTransport
{
    /// <summary>Awaits each bounded decoded snapshot and returns only a whole validated terminal reply.</summary>
    /// <param name="request">The exact local tool-free request.</param>
    /// <param name="publish">Backpressured provisional publication, without persistence or execution authority.</param>
    /// <param name="cancellationToken">The owned exchange lifetime.</param>
    /// <returns>The complete terminal outcome, never a partial success.</returns>
    Task<ModelToolTransportReply> CompleteWithAnswerAsync(ModelToolRequest request,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken);
}
