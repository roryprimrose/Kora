# Assistant and Activation Name

Status: configurable assistant display and local command name implemented;
production wake-word profiles remain proposed.

Related: [User Configuration](User_Configuration.md),
[Interaction Fallback](Interaction_Fallback.md), and
[Acceptance Criteria](Acceptance_Criteria.md).

## Implemented Product Behaviour

The assistant name defaults to "Kora". One device-local configured name is used
consistently for:

- Main and Settings window titles, branding, and explanatory text.
- System-tray Show, Settings, and Exit labels and the tray tooltip.
- Built-in command phrases, descriptions, and the optional spoken command
  prefix.
- Visual responses, spoken responses, listening guidance, and voice preview.

A committed custom name is custom-only. The previous name, including "Kora",
is retired as a command prefix and is not retained as a hidden recovery alias.
Tray and mouse controls remain available if the user cannot use the configured
name.

The active name is required to begin an unsolicited spoken turn, not to answer
every host-owned question. While the host question service has opened a
bounded conversational reply turn, a schema-valid answer such as "Yes", "No",
or an option name is accepted without a prefix and is bound to that exact
question/revision. Expiry returns to normal activation-name behavior. This
does not retain an old assistant name as an alias or create an unbounded
conversation microphone.

The product and host identity do not change. The executable remains `Kora.exe`;
assemblies, namespaces, icon resources, publisher/trust identity, internal IDs,
`%LOCALAPPDATA%\Kora`, `%APPDATA%\Kora`, and `kora-YYYYMMDD.log` remain fixed.
Internal diagnostics may identify the Kora product without changing
user-facing assistant identity.

## Settings and Persistence

The single-instance Settings window exposes the same observable assistant-name
state intended for future validated verbal/model mutations. The user enters a
name and explicitly chooses **Apply name**.

Implemented validation accepts 1-3 words and at most 32 Unicode characters.
Letters, numbers, spaces, apostrophes, and hyphens are supported. Whitespace is
trimmed and collapsed. Blank, over-length, over-word-count, punctuation, and
separator-only values are rejected visibly without changing the active name.
Names that would make two built-in phrases route to different actions are also
rejected before persistence.

The normalized value is atomically stored at
`%LOCALAPPDATA%\Kora\Preferences\assistant-name.txt`. A missing preference uses
"Kora". Invalid saved data is reported as a failed setting load rather than
silently substituted. Access and I/O failures leave the prior name active.

Every requested name mutation emits correlated configuration-write audit
events. Outcomes distinguish succeeded, denied invalid input, cancelled
no-change requests, access denial, and I/O failure. Audit records and ordinary
diagnostics do not include the chosen name.

## Local Command Switching

The current Windows bootstrap uses the installed local speech-recognition
engine with an exact host-owned grammar. It recognizes catalogue phrases with
or without the configured name prefix; it is not the final continuous
wake-word detector.

When a rename is committed while listening is active, capture is stopped and
restarted with grammar generated from the new name under the existing explicit
listening consent. The old name is absent from the replacement grammar. A
rename while listening is disabled never opens the microphone.

Until persistence succeeds, the previous name remains authoritative. If the
new grammar cannot start, the saved/display identity remains committed, capture
stays unavailable, and the failure is shown visually; the old grammar is not
silently restored.

## Future Verbal Rename

Once model-backed verbal settings are added, verbal rename must call the same
validated setting operation as the native Apply control. The model may propose
a value but cannot write the preference file, alter binary/product identity,
retain aliases, or bypass validation and audit.

Ambiguous spelling or pronunciation requires clarification before commit.
Material changes are shown exactly and any spoken acknowledgement follows the
effective speech, call, privacy, mute, and output-device policy. A preparation
or calibration utterance is not a task command or action approval.

## Production Wake-Word Requirements

Supporting a custom name in the current exact Windows command grammar does not
prove support for an always-on custom wake detector. A production detector must
advertise and validate the names/languages it actually supports and satisfy the
quality, false-activation, playback-rejection, privacy, and resource gates in
[Acceptance Criteria](Acceptance_Criteria.md).

Detector/profile preparation must be local and protected. It cannot modify
Kora binaries, embedded skills, trust policy, or executable loading roots, and
it cannot accept arbitrary model URLs, scripts, plugins, or paths from a model
or skill. Optional calibration audio is bounded, local, memory-only, and
discarded after the declared operation.

Atomic detector switching must pause capture, invalidate old callbacks, clear
old pre-roll, publish one new detector generation, and resume only when existing
listening consent and session policy permit it. Late results from a retired name
cannot route commands. A missing or corrupt custom profile reports activation
as unavailable and offers tray recovery; it never silently re-enables "Kora".

Distinct names can reduce office cross-activation but are not identity
verification. A colleague, similar-sounding speech, or playback may still
activate a detector. Speaker-confidence, private-output, call, approval,
context-consent, and tool-permission policy remain independent of the name.
