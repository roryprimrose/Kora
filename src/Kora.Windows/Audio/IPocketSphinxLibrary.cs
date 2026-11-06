namespace Kora.Windows.Audio;

/// <summary>Owns the pinned C ABI exports and their library lifetime.</summary>
internal interface IPocketSphinxLibrary : IDisposable
{
    /// <summary>Resolves a typed C ABI export.</summary>
    T Export<T>(string symbol) where T : Delegate;
}
