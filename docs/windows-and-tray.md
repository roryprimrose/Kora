# Windows, tray, and appearance

## Kora's presence

Kora's presence is the animated group of dots that communicates the assistant's
current state. Its particle cloud appears on a small, borderless, transparent
surface near the bottom-right of the primary display's working area. It floats
above ordinary windows without a rectangular application frame.

The presence is **click-through by default**: clicks, scrolling, and other mouse
events reach the window underneath it, even over the visible dots.
To reposition it, **hold Ctrl, then left-click and drag the visible presence**.
While Ctrl is held, the presence receives mouse events instead of passing them
through, and the pointer over it changes to the **four-way move cursor**, including
when it was already resting over the presence. Release the mouse button and Ctrl
to return to click-through mode.
Either Ctrl key works, and Kora does not need keyboard focus.
An in-progress mouse gesture stays with the window where it began; pressing or
releasing Ctrl mid-gesture takes effect after the mouse buttons are released.

Kora stores the position on
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

While idle or listening without a prompt needing attention, presence hides
automatically after **10 seconds** without Kora interaction. It does not ask
before hiding. Change **Presence timeout** under **Settings > Appearance >
Presence appearance** to any value from 1 to 60 seconds. New interaction
restarts the timer, including Ctrl-dragging the presence.
Active work, speech, approvals, questions, recovery actions, grant editing,
and unacknowledged failures keep presence visible. After these finish, the
timer starts fresh. Hiding presentation never disables listening.

When visual text is enabled or forced by a voice-output problem, Kora opens a
separate compact response surface near the presence. That surface contains
the response, last transcript, typed-command fallback, and a **Dismiss** action.
Drag its title area to reposition it. Kora remembers the position when it is
still on a connected display.

Under **Settings > Appearance > Visual feedback**, configure:

- **Always show** - keep the current response visible until dismissed;
- **Stay on top** - keep the response above other windows; enabled by default;
- **Response timeout** - change the response's 1-60 second inactivity timeout,
  default **5 seconds**.

Presence and response timeouts are independent. **Always show** keeps only
the response window visible; idle presence still uses its own timeout.

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
- **Sessions**
- **Preview clipboard (local plain text)**
- **Release maintenance (notify-only)**
- **Evidence (read-only)**
- **Review local version (native question)**
- Read-only generic **input status** (no endpoint ID/name or transcript in the tooltip)
- **Listening controls > Enable listening / Disable listening**
- **Voice consent / push-to-talk**
- **Microphones** (friendly endpoint labels, native selected-preference marks and availability)
- **Refresh microphones** (metadata only; five-second deadline; no model/network dependency)
- **Stop speaking**
- **Exit Kora**

Tray labels use the configured assistant name except for the fixed
Documentation label.

Selection does not release a privacy/manual-disable recovery hold. Tray clicks
never implicitly activate capture. Windows privacy events hide sensitive
Kora surfaces; unlocking alone does not reveal them or reopen input. Use the
launcher/tray to return to native status and recovery.

### Microphone and listening recovery

Open the right-click menu to refresh microphone metadata without recording.
Refresh is single-flight and has a five-second caller deadline. If Windows
enumeration is still finishing after a timeout, retry later; repeated clicks
do not start more workers. Failure reports **Microphone recovery needs
attention** with Refresh/Settings recovery, and capture stays closed.

The tooltip distinguishes PTT-ready (**microphone closed; wake unavailable**)
from actual PTT capture, disabled input, unavailable selection and denied/
unknown Windows privacy. It contains no device identity or private content.
The microphone submenu shows **System** and active named endpoints. A native
selection mark means saved preference, not recording. A missing saved pin
remains marked **unavailable; preference retained**, not replaced. System can
explicitly clear an unavailable pin even when Windows has no usable default.

Choosing an endpoint saves only that current revision's preference. Changed
selection closes input and requires explicit **Enable listening** afterwards;
it does not test the device, record audio or grant consent. A stale menu click,
changed Windows topology, lost ownership, locked/disconnected/unknown privacy
or a failed save reports refusal/recovery. Refresh and choose again.

**Enable listening** requires saved consent and fresh existing host gates; it
only arms PTT. **Disable listening** is always an explicit close, never a toggle
that can accidentally enable from an old menu. **Stop speaking** stops only
playback. **Voice consent / push-to-talk** opens existing Settings for consent,
readiness and held PTT. These actions do not cancel tasks or approve work.
No microphone test or ambient wake is provided by this recovery slice.

## Passive document details

### Minimal durable Sessions

Choose **Sessions**, use exact **open sessions**, or press **Ctrl+Shift+S**
in the compact response. **Refresh** lists existing Active/Done durable IDs
and generations with bounded user names and metadata revisions, 25 per page
(50 maximum at the store). Preserved legacy sessions are explicitly unnamed.
Selection reads actual typed question history and current task records, never
a fabricated conversation or progress.
Next session/question/task/evidence controls are independently bounded.
**Read selected evidence** uses the separate diagnostic/audit projection;
missing conversation/session sources remain unavailable.

Selection and browsing do not redirect questions, approvals, voice input or
the global command box, and never resume. **Mark selected ID Done** and
**Explicitly resume selected ID** are explicit actions on the displayed ID
and expected generation. Live/Unknown work and unresolved questions block
them; Kora does not cancel or abandon work to make them eligible. Ownership,
privacy, call policy/revision, committed control intent and atomic authoritative
audit are checked by the writer, not inferred from the display. A conflict or
failure reports refusal/recovery; refresh before retrying.

Enter a name and choose **Create empty Active session** to deliberately create
a new immutable session ID, Active generation 1 and metadata revision 1.
This creates no execution task, executor, model context, question, approval or
permission; Kora retains an administrative control intent/receipt for audit.
**Rename selected ID** applies only to the displayed exact ID and its observed
generation/metadata revision. Duplicate names are permitted, never ambiguous
authority. A stale edit is refused: refresh and select the exact ID again.
Rename is allowed on Done or unresolved-work sessions without resuming,
abandoning or resolving work, changing grants or extending meaningful activity.

Names must be nonblank NFC Unicode with no surrounding whitespace, at most
**120 Unicode scalars / 480 UTF-8 bytes**, with no control/format characters
or line/paragraph separators. Invalid names are refused, never silently
normalized/truncated. Names are intentional private content, stored only in
the existing private interaction partition, not raw audit/log/activity content.
The validated transactional v1-to-v2 schema upgrade preserves existing
authority/history and does not invent names. Missing/corrupt/unsupported data
is unavailable, never replaced with empty authority. Create/Rename require
the same fresh original-user ownership/privacy/call admission and required
atomic audit as lifecycle controls; protected/unknown call state denies them.

Done/resume each advance generation and invalidate old scoped authority.
Perpetual records remain independent. Resume does not rerun tasks, revive
approvals or restore/transmit old context. Privacy closure clears this window.
Full conversations, work queues, name-based voice targeting, automatic archive,
delete/retention, export and scheduling are not delivered. No live visual,
screen-reader or OS-session acceptance is claimed by fixture tests.

The compact command box and activated voice also expose bounded
[exact-ID session commands](commands.md#bounded-exact-id-session-commands).
They share this host service/store, never inherit the selected row/window,
and require explicit generation/metadata revisions for mutations. Structured
results are observations rather than runtime progress or conversation history.
Protected-call voice mutations remain unavailable; pending bootstrap questions
and approvals are preserved and must be resolved explicitly.

### Local clipboard preview

The separate [clipboard preview](commands.md#explicit-local-clipboard-preview)
uses inert native plain text, not Markdown/HTML parsing or the document viewer's
copy controls. It has exact-ID reuse and revoke/clear only. Closing it revokes
the selected snapshot. It does not authorize explanation or model transmission.

The separate **Review local version (native question)** tray entry opens one
owned native card bound to the original durable host question, not whichever
window is focused. It shows exact target/revision/expiry, an unselected choice,
Review exact record, Save draft, Submit answer, Cancel question and Close.
Editing is not submission; closing/Escape is not approval or grant use.
Expiry, revised/closed targets and unknown privacy/ownership disable the
affected card. Query/audit failures report recovery without a success receipt.
See [the bounded version route](commands.md#show-the-running-version).
This question card is separate from passive Sessions/history and is not a
new side effect or general approval dispatcher.

Choose **Documentation**, select a guide page, then **Open details** to read
that exact page in a separate native window. Its host-owned title, provenance,
revision, digest and sensitivity stay outside the document. Opening the same
page again activates its existing viewer; it does not regenerate the page or
silently replace an older revision.

Use the native Rendered/Source and search controls to inspect the complete
admitted item. Native headings and code are styled; emphasis is plain text,
and tables, images, HTML and diagrams use labelled exact-source fallback.
Use **Continuous text** for cross-block selection/search. **Ctrl+F** finds,
**F3** / **Shift+F3** navigates matches, **Ctrl+U** switches source, and
**Escape** closes only the viewer. **Ctrl+A** selects current continuous
content. **Copy selection** / **Ctrl+C** copies just the selected nonempty
range after the same access/disclosure checks; cut/paste are unavailable.
**Copy exact source** deliberately copies Unicode plain text
with the original Markdown, whitespace and line breaks. It is not rich HTML
copy. Copying discloses the text to other applications and possibly clipboard
history/sync. No page can initiate copy, fetch images, follow external links,
approve an operation or capture model context.

The native-text-v1 source limit is **256 KiB UTF-8**, with **512 blocks**,
**4,096 nodes**, **32 nesting levels**, and **8 open viewers**. Unsupported
content, missing rendering or structural limits show labelled exact-source
fallback; oversized/invalid input reports rejection without truncating it.
Search accepts at most **256 characters**. Closing/hiding details is
presentation only; it does not stop work, finish a session or delete source.
Privacy closure clears and closes details rather than restoring them on unlock.

This is an embedded-document reader, not durable conversation history or
general response routing. Full conversation/history, verbal detail offers, rich HTML,
diagrams, export and full script/diff review remain planned. Native visual and
assistive-technology trials remain pending.

Closing Settings, Documentation, details, or the visual response closes or hides only
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
