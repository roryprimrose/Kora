namespace Kora.Core.Context;

public sealed record LocalFileWhitespaceCounts(
    int CrLfPairs, int StandaloneLf, int StandaloneCr, int Tabs, int Spaces);
