using System.Collections.Immutable;

namespace Kora;

internal sealed record NativeTableData(
    int Number, ImmutableArray<string> Headers, ImmutableArray<ImmutableArray<string>> Rows);
