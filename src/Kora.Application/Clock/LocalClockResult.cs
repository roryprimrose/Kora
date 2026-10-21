using Kora.Core.Clock;

namespace Kora.Application.Clock;

public sealed record LocalClockResult(LocalClockOutcome Outcome, LocalClockAnswer? Answer = null);
