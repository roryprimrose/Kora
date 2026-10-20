namespace Kora.Core.Utilities;

/// <summary>Randomness seam for standalone utility output only; not host ID generation.</summary>
public interface IUtilityUuidGenerator
{
    Guid Generate();
}
