# Windows, tray, and appearance

## Kora's presence

Kora's presence is the animated group of dots that communicates the assistant's
current state. Its particle cloud appears on a small, borderless, transparent
surface near the bottom-right of the primary display's working area. It floats
above ordinary windows without a rectangular application frame.

Drag the visible presence to reposition it. Kora stores the position on
this device and restores it after restart when that display remains connected.
If the saved display is unavailable, the presence returns to the
bottom-right of an available working area.

The presence can be hidden while push-to-talk is armed without recording. It appears
when you interact with Kora, while Kora is processing work, or when a result
requires attention. Colour and motion reflect whether Kora is calculating,
waiting, executing, reporting information, succeeding, or failing.

The presence communicates state, not percentage complete. Hiding it does not
turn off listening or stop ongoing work. Microphone status is separate from
the presence's visibility.

While listening, the visible presence hides after 5 seconds without
interaction by default. Change **Visible timeout** under Appearance in
Settings to any value from 1 to 60 seconds. New interaction restarts the timer.

When visual text is enabled or forced by a voice-output problem, Kora opens a
separate compact response surface near the presence. That surface contains
the response, last transcript, typed-command fallback, and a **Dismiss** action.
Drag its title area to reposition it. Kora remembers the position when it is
still on a connected display.

Under **Settings > Appearance > Visual feedback**, configure:

- **Always show** - keep the current response visible until dismissed;
- **Stay on top** - keep the response above other windows; enabled by default;
- **Visible timeout** - change the shared 1-60 second inactivity timeout.

The presence always follows the visible timeout, even when **Always show**
keeps the response window visible.

## Tray icon

Kora keeps a notification-area icon while running.
Windows may initially place it under **Show hidden icons**. You can drag or pin
the Kora icon into the always-visible notification area using normal Windows
taskbar settings.

- Single left-click: show and activate Kora after the Windows double-click
  interval.
- Double-click: open or activate Settings.
- Right-click: open the tray menu.

The right-click menu contains:

- **Show Kora**
- **Kora Settings**
- **Documentation**
- **Enable listening / Disable listening**
- **Voice consent / push-to-talk**
- **Microphones** (enumerated endpoint IDs, selected and unavailable state)
- **Refresh microphones** (no model/network dependency)
- **Stop speaking**
- **Exit Kora**

Tray labels use the configured assistant name except for the fixed
Documentation label.

Selection does not release a privacy/manual-disable recovery hold. Tray clicks
never implicitly activate capture. Windows privacy events hide sensitive
Kora surfaces; unlocking alone does not reveal them or reopen input. Use the
launcher/tray to return to native status and recovery.

Closing Settings, Documentation, or the visual response closes or hides only
that surface. Kora remains available in the background until you choose
**Exit Kora** or use the supported exit command.

## Launching another build and returning

Installed and developer builds share a SID-scoped ownership domain. Ownership
is checked before application services, migration, tray or audio initialization.
A validated same-build launch activates the existing owner and forwards no task
arguments. An unreachable, incompatible or unverifiable owner is a blocker,
not permission to run a second assistant.

A different build remains a waiting candidate without microphone, model,
network work or a second assistant tray. The original presents a native
question with independently verified build/content and process identities.
Decline, expiry, lock or candidate death leaves ownership unchanged. Current
setup/reasoning/provider work must finish before handoff; it is not silently
killed. Approved transfer waits for actual desktop/service/audio quiescence.

After a safely released replacement exits, the original process acts only as a
native lifecycle supervisor and offers an explicit return. It has no assistant,
speech or capture services. Accept revalidates the exact original bytes and
normal ownership/readiness; decline or dismissal never restarts automatically.
An unclean-owner marker blocks crash takeover/return when orphaned work cannot
be reconciled; process death alone is not proof of worker quiescence. Follow the
native blocker rather than deleting it without checking interrupted work.

Developer builds use `%LOCALAPPDATA%\Kora\Development`, separate from installed
release data and microphone consent. Handoff does not transfer consent,
approvals, tasks or audio.

The bounded 2026-10-05 interactive trial verified same-build Release x64
activation, Debug x64 decline, accepted Debug/Release takeover and
exact-original Release return with one active tray/UI throughout. Exit during
active speech also completed cleanly after asynchronous provider shutdown was
made independent of Avalonia's retired UI context. Active-work refusal,
expiry, candidate death, lock during approval and crash recovery remain
unverified. A framework-dependent Release x86 candidate was blocked before
handoff because the x86 .NET Desktop Runtime was not installed; installer
acceptance must provision and verify that dependency before cross-architecture
takeover/return can close.

## Themes

Kora supports:

- **System** - follows Windows and changes while Kora is running;
- **Light**; and
- **Dark**.

The selected theme applies to Kora-owned visual surfaces, including this
Documentation window. Light and Dark are explicit overrides and do not change
the Windows system theme.

## Assistant identity

Renaming Kora changes visible and spoken assistant identity immediately:

- presence, response, and Settings titles;
- tray labels and tooltip;
- command prefix and recognition grammar;
- responses; and
- voice preview.

The program identity stays fixed as Kora. The executable remains `Kora.exe`.
