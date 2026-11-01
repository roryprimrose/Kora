using System.Runtime.InteropServices;

namespace Kora.Windows.Audio;

internal sealed class PocketSphinxLibrary : IPocketSphinxLibrary
{
    private readonly nint library;
    internal PocketSphinxLibrary(string path) =>
        library = NativeLibrary.Load(path, typeof(PocketSphinxLibrary).Assembly,
            DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
    public T Export<T>(string symbol) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));
    public void Dispose() => NativeLibrary.Free(library);
}
