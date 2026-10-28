namespace Kora.Core.Context;

/// <summary>Owns host permission admission before a native clipboard read.</summary>
public interface IClipboardReadWorkflow
{
    /// <summary>Reads only after an applicable tool grant is consumed and revalidated.</summary>
    Task<ClipboardOutcome> ReadAsync(Func<bool> eligible, CancellationToken token);
}
