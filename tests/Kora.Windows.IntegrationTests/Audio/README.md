# Hands-free test-only offline evaluation

## Evidence boundaries

The [production catalogue](../../../src/Kora.Windows/Audio/HandsFreeNativeProfileCatalogue.cs) deliberately supplies no qualified names and no capture qualification. [HandsFreeSetupTests](HandsFreeSetupTests.cs) retains that denial regression. Nothing here changes production preparation, composition, readiness, consent, routing or enablement.

[PocketSphinxKeywordConfigurationTests](PocketSphinxKeywordConfigurationTests.cs) uses synthetic dictionaries for literal mapping, ambiguity, strict UTF-8 and resource limits. [PocketSphinxKeywordDetectorTests](PocketSphinxKeywordDetectorTests.cs) uses managed fake exports through `IPocketSphinxLibrary`. Neither proves that the bundled native DLL works.

The supplied dictionary-only observation is `kora K AO R AH` at line 66219 of the 3275645-byte dictionary, SHA-256 `20B5C293E1F311FB375FE067E500EC5636F4FC7AF5594967263696DEF9B23BFE`. Its identity is pinned by [HandsFreeSetupCatalogue](../../../src/Kora.Windows/Audio/HandsFreeSetupCatalogue.cs); entry availability is not acoustic qualification. Normal tests do not require that dictionary.

## Normal deterministic checks

From the repository root, build the existing project with analyzers, without an implicit restore:

```powershell
dotnet build .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-restore
$runner = '.\tests\Kora.Windows.IntegrationTests\bin\Release\net10.0-windows10.0.19041.0\Kora.Windows.IntegrationTests.exe'
$classes = @(
  'PocketSphinxKeywordConfigurationTests', 'PocketSphinxKeywordDetectorTests',
  'HandsFreeOfflineEvaluationTests', 'HandsFreeSetupTests',
  'HandsFreeNativeReadinessTests', 'HandsFreeNativePipelineTests'
)
$arguments = @('-explicit', 'off')
foreach ($class in $classes) { $arguments += @('-class', "Kora.Windows.IntegrationTests.Audio.$class") }
& $runner @arguments
```

The [installed xUnit v3 in-process runner](../Kora.Windows.IntegrationTests.csproj) exposes `-explicit off` (the default), `-explicit only`, `-class`, `-method` and `-list full`. Run `& $runner --help` to inspect its own help; help returns exit code 2. This is the executable's CLI, not an invented `dotnet test` switch. Discovery does not execute native cases:

```powershell
& $runner -explicit only -class Kora.Windows.IntegrationTests.Audio.HandsFreeOfflineEvaluationTests -list full
```

Ordinary cases perform bounded local file I/O and fake-export calls. They never instantiate the physical microphone, load PocketSphinx or initialize Silero. Synthetic artifacts are retained under the owning checkout's ignored `.scratch\offline-tests\<unique-id>`; they contain generated data only. No packages, projects or gates are added.

## Separately authorized actual-native execution

For a bundled keyword-engine smoke check only, no recorded/annotated corpus is
consumed. Use the same thirteen exact `AssetsRoot` files and isolated
`NativePath` inventory described below. After separately authorizing native
loading, select the explicit asset-only method:

```powershell
$env:KORA_OFFLINE_ASSETS_ROOT = 'Q:\approved-offline\assets'
$env:KORA_OFFLINE_NATIVE_PATH = 'Q:\approved-offline\native\pocketsphinx-win-x64.dll'
& $runner -explicit only -method Kora.Windows.IntegrationTests.Audio.HandsFreeOfflineEvaluationTests.ActualNativeAssetsOnlySilenceSmoke
& $runner -explicit only -method Kora.Windows.IntegrationTests.Audio.HandsFreeOfflineEvaluationTests.ActualNativeAssetsOnlySileroSilenceTimeout
```

It verifies every compiled asset identity, loads the actual bundled DLL, feeds
one second of generated zero-valued PCM and confirms no wake. It captures and
plays no audio, uses no transcript, and grants no production qualification.
Missing or altered assets fail. Its `kora-native-silence-v1` receipt is a
negative native smoke observation, not acoustic, VAD, ASR or playback evidence.
The corpus-bound smoke and seven replay cases remain separately selected.
The separate Silero smoke loads the pinned ONNX model and feeds generated
silence until the default five-second no-command timeout. It requires no early
endpoint and completion at the first 512-sample boundary at or after 80000
samples. Its `kora-native-silero-silence-v1` receipt proves only that bounded
negative native path, not speech discrimination or real microphone timing.
The pinned native engine declares sample rate as an integer configuration
parameter. The adapter uses the integer setter/getter for both 16000-Hz sample
rate and 100-fps frame rate; a floating getter returns zero for this parameter.

The keyword likelihood-ratio cutoff is compiled host profile metadata, not microphone consent or acoustic qualification. The adapter explicitly sets and checks `kws_threshold` after native initialization. It must be finite and positive; it is not a probability capped at one.

The Kora candidate uses `1e30`, shared with the evaluation profile. A small generated corpus distinguished its Kora examples from a confusable phrase at this cutoff; that observation is not physical recall, a measured false-activation rate or release qualification. Existing profiles without a supplied cutoff retain the prior native default `1e-30`. Production capture remains unavailable.

**Do not run the following under a deterministic-only grant.** Native evaluation requires separate explicit permission, already available exact assets, independently approved audio fixtures and their retention decision. No code here downloads, installs assets into application data, captures a device, plays sound, invokes an application router or dispatches a conversation/tool effect.

[HandsFreeOfflineFixture](HandsFreeOfflineFixture.cs) admits one immutable hash-approved manifest, at most 65536 bytes. Unknown fields/versions, incomplete/extra inventories, relative/network/reparse paths, changed identities and missing files fail before native loading. The speech file binder pins directories and rejects hard-linked corpus/manifest files; verified files stay open read-only.

- `AssetsRoot`: absolute flat directory containing exactly the thirteen names and identities from [HandsFreeSetupCatalogue](../../../src/Kora.Windows/Audio/HandsFreeSetupCatalogue.cs). This is an evaluation directory, not a prepared application partition.
- `NativePath`: absolute `pocketsphinx-win-x64.dll` in a separate directory containing only that file; exactly 668672 bytes, SHA-256 `725609787580AA759A9D9A62B1A7E6136408346D159D9E4681AFA1AF9FFAE69B`.
- `CorpusRoot`: absolute flat directory containing exactly the seven declared PCM files. No WAV headers: signed little-endian PCM16, 16000 Hz, mono, at most 30 seconds per file.
- Each fixture needs independent identity/provenance and consent review. The manifest's approved digest binds annotations and identities; it is not itself consent, acoustic evidence or production qualification.

The exact schema uses case-sensitive property names:

```json
{
  "Version": 1,
  "AssetsRoot": "Q:\\approved-offline\\assets",
  "NativePath": "Q:\\approved-offline\\native\\pocketsphinx-win-x64.dll",
  "CorpusRoot": "Q:\\approved-offline\\corpus",
  "Cases": [
    {
      "Id": "immediate",
      "File": "immediate.pcm",
      "Bytes": 320000,
      "Sha256": "<approved 64-hex SHA-256>",
      "ChunkSamples": 320,
      "FirstFrameMinimum": 10,
      "FirstFrameMaximum": 12,
      "LastFrameMinimum": 35,
      "LastFrameMaximum": 37,
      "CommandEndMinimum": 64000,
      "CommandEndMaximum": 80000
    }
  ]
}
```

This abbreviated example is intentionally **not admissible**: replace its digest and supply all seven cases. Bounds are fixture annotations, not recommended thresholds. `FirstFrame*` and `LastFrame*` are inclusive native frame bounds; command-end bounds are absolute exclusive sample offsets in the original file.

| Required Id / filename stem | Meaning | ChunkSamples |
| --- | --- | --- |
| `immediate` | Kora immediately followed by command speech | 320 |
| `paused` | Kora, pause, then command speech | 320 |
| `no-command` | Kora with enough silence to exercise the real command-start timeout | 320 |
| `similar` | Similar words without Kora; no wake expected | 320 |
| `negative` | Speech/noise without Kora; no wake expected | 320 |
| `repeated` | Two utterances; only the first wake can open capture | 320 |
| `chunk-split` | Immediate command with boundaries crossing non-frame-aligned chunks | 127 |

Negative cases require all frame/command annotations to be zero. Positive cases require a nonempty post-name interval, bounded command end and additional tail PCM after that end to test exclusion. A corpus missing trailing silence, a real wake or the expected VAD endpoint fails; EOF is never fabricated as an endpoint.

After separate authorization, explicitly select the native class/method and explicit mode:

```powershell
$env:KORA_OFFLINE_MANIFEST = 'Q:\approved-offline\manifest.json'
$env:KORA_OFFLINE_MANIFEST_SHA256 = '<independently approved manifest SHA-256>'
& $runner -explicit only -method Kora.Windows.IntegrationTests.Audio.HandsFreeOfflineEvaluationTests.ActualNativeSilenceSmoke
& $runner -explicit only -method Kora.Windows.IntegrationTests.Audio.HandsFreeOfflineEvaluationTests.ActualNativeFileReplay
```

These are eight selected native cases: one silence smoke and seven replay cases. A selected run without the manifest, assets or corpus **fails**, rather than skipping, returning success or substituting fake detection. Silence smoke is negative evidence only.

[HandsFreeOfflineEvaluationTests](HandsFreeOfflineEvaluationTests.cs) uses the production detector, Silero endpoint and Windows hands-free session. Its file-backed microphone substitutes only the device. A command-only PCM probe checks post-name samples, detector intervals, stale-generation denial, once-only wake consumption and no second capture from later PCM.

Corpus streams are opened from verified native handles, so their `FileStream.Name` is not a usable pathname. Replay reopens the canonical manifest path while the original file and ancestor leases remain pinned. It pauses file pumping at the synchronous wake activation notification, then awaits the wake task; asynchronous task scheduling cannot consume and overwrite the bounded pre-roll.

The separately selected endpoint comparison feeds the same command interval in 127-, 320-, 512- and 2240-sample chunks through the real VAD. It verifies endpoint completion and bounded chunk-rounding differences, not acoustic accuracy:

```powershell
& $runner -explicit only -method Kora.Windows.IntegrationTests.Audio.HandsFreeOfflineEvaluationTests.ActualNativeEndpointPreservesSpeechAcrossChunkBoundaries
```

The probe does **not** recognize speech, fabricate transcripts or prove first-word ASR accuracy. Windows ASR is not implemented by this corpus contract; adding independently approved recognizer fixtures needs a separate decision.

Existing deterministic [pipeline regressions](HandsFreeNativePipelineTests.cs) retain cancellation, revocation, open/decode/join failure, late-result and resource-quiescence coverage.

## Receipts and limitations

Successful native cases emit a `kora-offline-evaluation-v1` observation: evaluation-only marker, assembly identity/runtime/architecture, manifest digest, asset/fixture identities, case, 16000-Hz/100-fps configuration, sample count, inclusive frame interval, exclusive command start/end, capture count, quiescence and terminal `passed` status. No audio/transcript content is emitted.

Retain the source revision, exact invocation, build/test output and runner failure result alongside that observation in approved scratch. Missing-input/assertion/native failures remain failed xUnit results, not success receipts. Unknown native closure is not permission to retry or release dependent resources.

Native, ASR, acoustic, physical microphone, playback-safety and hardware/provider qualification are **unrun** by this increment. No wake recall, false-activation rate, TTS safety, production readiness or Slice A completion is claimed. [Speech Input](../../../Design/Speech_Input.md) and [acceptance criteria](../../../Design/Acceptance_Criteria.md) remain authoritative.
