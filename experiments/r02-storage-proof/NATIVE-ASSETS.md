# Native-asset and licensing assessment

This is package-selection evidence, not legal approval or production admission.
No native binaries are checked in. All dependency references are local to this
experiment and pinned by [the lock file](packages.lock.json).

## Observed stack

| Layer | Selected experiment version | License / admission implication |
|---|---|---|
| Microsoft.Data.Sqlite.Core | 10.0.12 | MIT; no bundled default native SQLite |
| SQLitePCLRaw.bundle_e_sqlcipher/provider/lib | 2.1.11 | NuGet metadata: Apache-2.0; native package explicitly says unofficial and unsupported |
| SQLitePCLRaw.core | 2.1.12, resolved by Microsoft.Data.Sqlite.Core | Apache-2.0; lock captures the mixed bundle/core patch versions actually tested |
| Native SQLCipher | **4.5.2 community**, actual `PRAGMA cipher_version` | BSD-style SQLCipher terms; wrapper metadata alone does not cover the embedded code |
| Embedded SQLite | **3.39.2**, actual `sqlite_version()` | SQLite public domain; old baseline, not the current production SQLite choice |
| Crypto provider | **LibTomCrypt 1.18.2**, actual cipher provider/version | Dual public-domain/WTFPL choice in upstream version's LICENSE; dependency provenance/notice review still required |
| System.Security.Cryptography.ProtectedData | 10.0.12 | .NET MIT; real Windows DPAPI for this Windows-only proof |
| AES-GCM / HMAC-SHA256 | .NET 10.0.12 runtime | No additional cryptographic NuGet implementation; platform/runtime crypto remains part of deployment |

SQLCipher uses page authentication (the run checks `cipher_use_hmac=1`), 4096
byte cipher pages and a 256,000-iteration password KDF. The measured Windows
build uses LibTomCrypt, **not OpenSSL**. Do not infer provider or linked-library
licensing from the SQLCipher brand; repeat provider checks for every RID/build.

**Reject this native package for production** without a separately reviewed,
maintained replacement and its vulnerability/provenance assessment. The package
version is not the embedded SQLCipher/SQLite version. A successful NuGet audit
or managed restore does not establish that bundled native code is current or
free of vulnerabilities. No exploitable-vulnerability assessment is claimed.

## Practical alternatives

- Maintained official Zetetic Commercial/Enterprise builds offer support under
  their own terms. Trials are for evaluation and cannot be redistributed as
  production; no commercial/trial package was obtained or tested.
- A maintained Community source build is possible under BSD-style notices but
  requires pinned SQLCipher, SQLite and crypto source provenance, reproducible
  toolchains and per-RID build/runtime tests. No self-built native library was
  produced here.
- SQLite SEE is a separately licensed native alternative. Its documentation
  distinguishes OFB from authenticated CCM/GCM variants; "encrypted" alone is
  insufficient for the tamper requirement. It was assessed from public docs,
  not acquired, licensed or benchmarked.
- Application AES-GCM envelopes avoid a page-codec dependency, but still need
  maintained native SQLite, correct encryption of every content-bearing
  surface, constrained search and explicit metadata/equality leakage limits.
  This experiment's envelope benchmark uses the unkeyed SQLCipher engine,
  not a new recommended native distribution.

## Publication evidence and limits

The local runner publishes framework-dependent win-x64 and win-x86
outputs, checks the native PE machine values, and records byte counts and
SHA256 in [the asset report](evidence/native-assets.json).
Windows x64 native code is actually loaded by the proof; x86 assets are
publication-only evidence. The runner and full proof intentionally require
Windows DPAPI and ACLs. Linux is not a supported product OS at this stage.

The restored native package includes other architectures and operating systems
besides the selected Windows x64/x86 assets. Package availability does not
imply supported product platforms or proven loadability.
Avoid shipping every RID from a portable build; select explicit deployment
RIDs. Bundling a SQLCipher provider alongside production's default SQLite
provider risks competing global SQLitePCL initialization/native assets; this
experiment is isolated in its own process and never modifies composition.

The owner confirmed **Windows-only support on 2026-10-05**. Linux runtime
support and Linux-host cross-build validation are outside this experiment's
scope, not readiness blockers. Existing CI uses a Linux build host to publish
Windows application assets; that does not imply Linux product support. This
experiment does not change that pipeline or add a requirement to reproduce it
on a local Linux host.

Selecting prebuilt Windows assets is not proof of their source build provenance,
CRT/import requirements, signatures, installer access controls or clean-machine
runtime load. Installed Windows x86/x64 native dependency resolution remains
an open gate, and DPAPI runtime tests require actual Windows. No host bootstrap,
container service, account or shared pipeline was changed. The resulting
native admission requirements are recorded in the
[canonical storage direction](../../Design/Architecture.md#windows-durable-storage-direction).
Do not bypass existing required CI checks/reviews or treat this
directory as shipping composition.

## Source references (consulted 2026-10-05)

- [Microsoft.Data.Sqlite encryption/provider integration](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/encryption)
- [SQLitePCLRaw native package 2.1.11](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlcipher/2.1.11)
- [SQLCipher security design and temporary-file warning](https://www.zetetic.net/sqlcipher/design/)
- [SQLCipher Community licensing requirements](https://www.zetetic.net/sqlcipher/community/)
- [SQLCipher 4.5.2 LICENSE](https://github.com/sqlcipher/sqlcipher/blob/v4.5.2/LICENSE)
- [LibTomCrypt 1.18.2 LICENSE](https://github.com/libtom/libtomcrypt/blob/v1.18.2/LICENSE)
- [Commercial/trial license distinctions](https://www.zetetic.net/sqlcipher/license/)
- [SQLite SEE algorithms/build/license](https://www.sqlite.org/see/doc/trunk/www/readme.wiki)
- [Windows CryptProtectData boundary and roaming-profile exceptions](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)

Any future redistributed native build must carry the complete applicable
licenses/notices in user-accessible application/distribution materials.
Links in this experimental assessment are not a substitute for those notices.
CurrentUser DPAPI is not a hard device-binding guarantee under all Windows
profile/domain configurations; explicit device binding, migration, backup and
profile-loss recovery need their own reviewed contract.
