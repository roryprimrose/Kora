using Kora.Core.Utilities;

namespace Kora.Tools.Utilities;

public sealed class RandomUtilityUuidGenerator : IUtilityUuidGenerator
{
    public Guid Generate() => Guid.NewGuid();
}
