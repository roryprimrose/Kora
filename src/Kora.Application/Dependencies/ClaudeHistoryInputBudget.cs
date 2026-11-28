using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

internal sealed record ClaudeHistoryInputBudget(
    Func<ModelContextEnvelope, CancellationToken, Task<int>> Measure,
    Func<CancellationToken, ValueTask<bool>> Current);
