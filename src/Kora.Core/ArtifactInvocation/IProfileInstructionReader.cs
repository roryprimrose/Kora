namespace Kora.Core.Artifacts;

/// <summary>Opens only an explicitly selected personal instruction original, without reading its content.</summary>
public interface IProfileInstructionReader
{
    /// <summary>Acquires a bounded metadata-only lease for one supported personal source.</summary>
    Task<IProfileInstructionSelection> OpenSelection(string selectedPath, CancellationToken token);
}
