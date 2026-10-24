namespace Kora.Core.Providers;

/// <summary>Build-owned release authority. Neither user input nor local preferences may add trusted evidence.</summary>
public interface ILocalModelQualificationAuthority
{
    /// <summary>Returns complete, integrity-verified release attestations; observation never qualifies a profile.</summary>
    Task<IReadOnlyList<LocalModelQualificationEvidence>> Read(CancellationToken cancellationToken);
}