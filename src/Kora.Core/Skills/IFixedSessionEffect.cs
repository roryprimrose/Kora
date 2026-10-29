namespace Kora.Core.Skills;

public interface IFixedSessionEffect
{
    string? CurrentIdentityDigest { get; }
    FixedSessionEffectResult Request(FixedSessionAction action);
}