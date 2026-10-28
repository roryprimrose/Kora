# Bundled hands-free CPU library

This is the Windows x64 PocketSphinx **5.1.1** C library built from the official
commit `511126b492dcb267cf30d49d631946d7b61a9530`. It is application code, not an
optional executable download. The complete upstream licence, including incorporated
code notices, is in [pocketsphinx-licence.txt](pocketsphinx-licence.txt) and is
copied beside the DLL in build/publish output.
The payload is included only in Windows x64 and RID-less development outputs,
not in the unsupported x86 distribution.

The frozen DLL is 668,672 bytes, SHA-256
`725609787580AA759A9D9A62B1A7E6136408346D159D9E4681AFA1AF9FFAE69B`.
The authoritative consumer identity is in
[HandsFreeSetupCatalogue](../HandsFreeSetupCatalogue.cs).
It was built with Visual Studio 18 / MSVC 14.51.36260.0, x64 Release,
static multithreaded CRT. `dumpbin /dependents` lists **KERNEL32.dll only**.
No Python, sphinxbase DLL, GPU toolkit or downloaded native dependency is needed.
Existing ONNX Runtime and Windows recognition dependencies remain unchanged.

Rebuild from the exact pinned source, not a moving tag or third-party wheel:

```powershell
cmake -S <pinned-source> -B <owned-build-directory> -G "Visual Studio 18 2026" -A x64 -DBUILD_SHARED_LIBS=ON -DBUILD_TESTING=OFF -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DCMAKE_DISABLE_FIND_PACKAGE_BISON=TRUE -DCMAKE_DISABLE_FIND_PACKAGE_FLEX=TRUE
cmake --build <owned-build-directory> --config Release --target pocketsphinx --parallel 4
dumpbin /dependents <owned-build-directory>\Release\pocketsphinx.dll
dumpbin /exports <owned-build-directory>\Release\pocketsphinx.dll
```

Changing compiler/build output requires a new exact native digest and dependency
review; source identity alone cannot bless different bytes. The release includes
the compiled payload, so application builds do not download or rebuild upstream.
The acoustic/dictionary/VAD files are separately reviewed optional data, acquired
through the existing private speech-component store. Their aliases are explicit
native configuration paths, not a second model directory or persistence store.
No general language model is included in the offer.

Preparation verifies the production binder's complete closed file inventory,
including native bytes, data, notices and the ownership marker, without loading
the DLL or running a model. This does **not** establish keyword accuracy,
configured-name support, playback exclusion or acoustic qualification.
The production profile has no qualified names and cannot open capture.
