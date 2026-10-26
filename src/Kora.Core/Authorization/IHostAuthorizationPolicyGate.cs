namespace Kora.Core.Authorization;

/// <summary>Serializes answer/admission commit with changes to live host policy.</summary>
public interface IHostAuthorizationPolicyGate
{
    T WithCurrent<T>(HostAuthorizationPolicy baseline, Func<HostAuthorizationPolicy, T> commit);
}
