namespace Kora.Core.Dependencies;

/// <summary>Admits one reviewed local-only, tool-free target before a consumer binds any source envelope.</summary>
public interface IModelContextRunner
{
    /// <summary>Resolves genuine session/task authority. Source freshness callbacks supplement, never replace, host authority.</summary>
    /// <remarks>
    /// The caller must obtain affirmative source-specific sensitivity and source-use admission before constructing
    /// any envelope or evidence. This runner supplies no detector or sensitivity approval; preview, freshness,
    /// durable task admission and local-only execution do not confer source-use authority. Missing source policy
    /// must remain unavailable, not an implicitly approved callback.
    /// The live original input must match the target's SourceRequest; that volatile lineage does not confer target ownership.
    /// </remarks>
    Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
        CancellationToken token, Func<CancellationToken, Task>? validateSource = null);
}