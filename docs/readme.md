# Kora user guide

Kora is a Windows-only, local-first assistant with typed commands, explicit
push-to-talk, local speech and visual responses.

Exact built-in commands need no model, cloud account or network connection.
A separately consented and verified local model can answer unmatched requests
or propose existing actions. Choosing a clarification answer never approves
an action. Script-backed skills, general application launching, production wake
and a generic model tool/result loop remain unavailable.

## Start here

Follow [Getting started](getting-started.md) for requirements, first launch and
setup. To try a command:

1. Enter **help** in the typed command box; or
2. Review voice consent, hold **Push to talk**, say **"Kora, what can you do?"**,
   then release.

If you renamed the assistant, use the configured name. Saved consent can arm
push-to-talk at startup, but does not record ambient audio. **Disable listening**
releases the microphone; device selection alone does not undo that choice.

## Guide

| Topic | Read |
|---|---|
| Requirements and first launch | [Getting started](getting-started.md) |
| Microphone, voices and output | [Voice and audio](voice-and-audio.md) |
| Output modes and manual call protection | [Responses and calls](responses-and-calls.md) |
| Preference controls, defaults and limits | [Settings](settings.md) |
| Exact phrases and parameters | [Voice and typed commands](commands.md) |
| What actions and skills can do now | [Tools and built-in skills](tools-and-built-in-skills.md) |
| Categorized definition editing and disabling | [Customizations](customizations.md) |
| Single-file owned instruction editing | [Instruction editor](instruction-editor.md) |
| Sessions, windows and tray recovery | [Windows and tray](windows-and-tray.md) |
| Accessible presence states and speech playback | [Presence accessibility](presence-accessibility.md) |
| Explicit memory for later local conversations | [Device-profile memory](profile-memory.md) |
| Where data lives and using multiple computers | [Data storage and roaming profiles](data-storage.md) |
| Consent, grants, retained data and diagnostics | [Privacy, safety, and logs](privacy-safety-and-logs.md) |
| Review one diagnostic page and ask locally | [Evidence answers](evidence-answers.md) |
| Future execution design | [Skill and task execution design](skill-and-task-execution-design.md) |
| Problems and recovery | [Troubleshooting](troubleshooting.md) |

## Open this guide

Choose **Help > Documentation** from the tray, type **open documentation**, or use
push-to-talk and say **"Kora, open documentation"**.

## Important limitations

- Exact commands are not fuzzy matching. See [Commands](commands.md) for accepted
  variants and the different approval behavior of direct and model-suggested
  actions.
- Shutdown and computer-restart commands display proposals only; they do not
  request Windows power changes.
- Automatic call detection is unavailable. Use [manual call mode](responses-and-calls.md).
- Ordinary responses during microphone capture are visual. Spoken previews and
  approval prompts close capture; a new voice request needs a new PTT activation.
- File/clipboard inspection does not select model context or grant transmission
  consent. See [Privacy](privacy-safety-and-logs.md) before using retained content.
