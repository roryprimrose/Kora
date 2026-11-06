using Kora.Application.Context;
using Kora.Core.Context;

namespace Kora;

internal sealed record ClipboardQuestionActions(
    Func<string, ClipboardQuestionReview> Review,
    Func<ClipboardQuestionReview, CancellationToken, Task<ClipboardExplanationResult>> Answer);
