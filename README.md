# Kora

A Windows-only, local-first desktop assistant built with .NET 10 and Avalonia.
Kora accepts typed and explicitly activated voice requests, provides local
speech and visual responses, and keeps execution and consent under host control.

**Development status:** the runnable bootstrap has bounded commands, local
inference and native inspection/workspace features. It is not the complete
design or a qualified release. Script-backed skills, general application
launching, production wake and a generic model tool/result loop remain
unavailable. See the [current roadmap](Design/Implementation_Roadmap.md) for
delivered scope and remaining work.

## Start here

| I want to... | Read |
|---|---|
| Use Kora | [Getting started](docs/getting-started.md) and the [user guide](docs/readme.md) |
| Check current feature availability | [Feature overview](features.md) |
| Find an exact command | [Voice and typed commands](docs/commands.md) |
| Change a preference | [Settings](docs/settings.md) |
| Understand privacy and approvals | [Privacy, safety, and logs](docs/privacy-safety-and-logs.md) |
| Build or contribute | [Contributing](CONTRIBUTING.md) |
| Add an action or definition | [Extending Kora](EXTENDING-KORA.md) |
| Understand the architecture or planned product | [Design index](Design/README.md) |

## Run from source

Developer builds require Git, PowerShell 7 and the .NET 10 SDK pinned in
[global.json](global.json). On Windows:

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet tool restore
dotnet run --project .\src\Kora\Kora.csproj
```

Windows 10 build 19041 or later is required; Windows 11 is recommended.
Voice input needs an English Windows speech-recognition language and a
microphone. Spoken output needs an installed Windows voice or the separately
downloaded local Kokoro provider.

Typed exact commands and visual responses work without speech hardware,
a model, a cloud account or a network connection. Unmatched requests need a
verified local Ollama model. Setup and voice capture require separate consent;
follow [first launch](docs/getting-started.md#first-launch).

Precompiled framework-dependent binaries do not require Git or an SDK.
The [distribution contract](Design/Distribution_And_Updates.md) describes
runtime prerequisites and the managed-source build-only workflow; the
[installer guide](installer/README.md) describes MSI/Burn packaging.
Publication and passing CI are not installed or capability qualification.

## Development

[Contributing](CONTRIBUTING.md#development-checks) owns build, test, coverage,
logging and dependency-change requirements. Portable tests run on Linux;
non-destructive Windows integration tests and separately approved native
qualification cover different boundaries.

The [architecture](Design/Architecture.md#platform-boundaries-and-support)
defines project dependencies. Core owns portable contracts and policy,
Tools owns C# actions, Definitions owns bundled content, Application owns
orchestration, Windows owns native mechanisms, and the desktop project composes
and presents them.

## License

Kora is **source-available**, not OSI-approved open source, under the
[PolyForm Shield License 1.0.0](LICENSE). It permits qualifying personal and
internal commercial use, modification and distribution, but not providing
a competing product, whether paid or free. The license text governs.

Third-party components retain their own terms. See
[third-party notices](THIRD-PARTY-NOTICES.md) and the
[dependency-license policy](DEPENDENCY-LICENSES.md).
