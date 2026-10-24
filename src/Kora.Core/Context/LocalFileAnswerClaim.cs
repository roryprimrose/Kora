using System.Collections.Immutable;

namespace Kora.Core.Context;

public sealed record LocalFileAnswerClaim(string Text, ImmutableArray<LocalFileCitation> Citations);
