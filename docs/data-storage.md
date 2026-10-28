# Data storage and roaming profiles

Kora keeps conversations and device settings on your computer. Definition
files can follow your Windows roaming profile, when your organisation has
configured one. Kora does not run its own synchronisation service.

## What goes where?

| Data | Usual location | What to expect |
|---|---|---|
| Conversations, submitted requests, answers, attachments and reviewed memories | `%LOCALAPPDATA%\Kora` | Local databases and managed copies. They do not appear on another computer through Kora. Device-profile memory means this device's Kora profile, not all your computers. |
| Settings, permissions, source registrations and approvals | `%LOCALAPPDATA%\Kora` | Small settings files and local databases. Each computer has its own choices and permissions. |
| Kora instructions, prompts, skills and agents | `%APPDATA%\Kora` | Markdown files and declarative packages. Windows profile management may roam these files. |
| User definitions shared with other applications | Their existing personal profile folders | Their location and your organisation's policies determine whether they roam. Kora does not move the originals. |
| Diagnostics, logs and downloaded speech assets | `%LOCALAPPDATA%\Kora` | Local operational data, separate from conversations and definitions. |

These are the usual Windows locations; managed profiles can change where
data is actually held. Some personal skill folders, such as `.copilot` and
`.agents` beneath your user folder, are not automatically AppData roaming folders.

Unsent conversation text stays in memory per session; it is not autosaved.
Closing Kora can discard it. Previously saved legacy text has separate recovery.

Private local folders protect the Windows account boundary. Not all files or
databases are encrypted: do not copy the data folder to a shared location.
Do not put passwords or secrets in definition files.

## Using Kora on more than one computer

Windows controls whether and when roaming files arrive. Signing into the same
account is not, by itself, a promise of synchronisation. Changes may not arrive
while both computers are running.

An arriving definition is not permission to use your microphone, connect to a
service or run a tool. Review the current definition on the receiving computer
and enable its use there; other source and capability checks still apply.
Changed or incomplete definitions may be unavailable until reloaded and reviewed.
After upgrading, existing definitions may also need a one-time local review.

The two computers have independent conversations and work. A task or pending
approval on one computer is not resumed on the other. Kora's local-only model
mode does not turn off Windows-managed profile synchronisation.

## When files change elsewhere

If a definition changes while you edit it, keep your draft and compare it with
the current file before saving. Choose whether to reload, keep your draft or
save a supported new copy. Overwriting needs another review.

Avoid editing the same definition on both computers at once. Windows may resolve
competing copies before Kora sees them; Kora cannot recover an overwritten
version that it never observed. Some network-redirected profile locations are
not supported by Kora's native definition writers.

Do not synchronise or replace Kora's live database files manually. For a managed
profile deployment, ask your administrator which folders are included.

## Retention and deletion

Conversation retention, reviewed memory, definitions, grants and logs have
separate lifetimes. Deleting a conversation does not remove your definition
files or independently retained device-profile memories.

Deleting or editing a roaming definition can affect your other computers when
Windows next synchronises. It does not erase backups or guarantee removal of
offline copies.

See [Privacy, safety, and logs](privacy-safety-and-logs.md),
[Customizations](customizations.md) and [Device-profile memory](profile-memory.md)
for the corresponding controls.
