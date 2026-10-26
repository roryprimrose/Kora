using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Tools.Files;

public sealed partial class LocalFilePreview
{
    internal Task<TResult> UseFolderAnswerAsync<TResult>(LocalFolderReference exactSource, Func<bool> gate,
        Func<LocalFileAnswerOutcome, TResult> empty,
        Func<LocalFolderRevision, long, Func<bool>, CancellationToken, Task<TResult>> work,
        Action<TResult> commit, CancellationToken token) =>
        UseCurrentAsync(() => folderRevision?.Reference == exactSource ? folderRevision : null,
            source => source.Review.Request, gate, outcome => empty(outcome switch
            {
                LocalFileSearchOutcome.Busy => LocalFileAnswerOutcome.Busy,
                LocalFileSearchOutcome.Stale => LocalFileAnswerOutcome.Stale,
                LocalFileSearchOutcome.Cancelled => LocalFileAnswerOutcome.Cancelled,
                LocalFileSearchOutcome.Unavailable => LocalFileAnswerOutcome.Unavailable,
                _ => LocalFileAnswerOutcome.Denied,
            }), work, commit, token);
}
