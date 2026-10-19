namespace Kora.Core.Dependencies;

/// <summary>Admits one reviewed local-only, tool-free target before a consumer binds any source envelope.</summary>
public interface IModelContextRunner
{
    /// <summary>Resolves genuine session/task authority. Source freshness callbacks supplement, never replace, host authority.</summary>
    Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
        CancellationToken token, Func<CancellationToken, Task>? validateSource = null);
}