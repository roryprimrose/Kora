# Session action permission levels

Select an existing conversation in the primary Sessions pane or Session
management. **Exact-session action permission level** shows its current policy.
Choose **Read current policy** to refresh an unavailable or stale observation.

1. Select **Review all**, **Model review**, or **Approve all**.
2. Select **Review change**. Check the named session, old/new level and warning.
3. Select **Confirm exact-session change**, or **Cancel review**. If the current level is already **Approve all**, **Review change** commits the exact protected change without another prompt, with a required session audit ([control](../src/Kora/SessionPermission/SessionsViewModel.SessionPermission.cs)).

Choosing a level or reading its policy does not change permission, execute an
action, create a grant, or change another session. Confirmation requires a fresh
original native user action and current private desktop ownership. A model/tool
callback or voice request cannot relabel itself as that native action.

## Typed and activated-voice changes

Use the exact conversation ID shown in Sessions, not its name or the foreground
window. These commands complete the same policy change without opening another
native confirmation:

```text
session permission status <session-id>
session permission review <session-id> review-all
session permission review <session-id> model-review
session permission review <session-id> approve-all
session permission confirm <session-id>
session permission cancel <session-id>
```

Review shows the conversation, old/new level, original channel and warning.
Nothing changes until separate confirmation, within 60 seconds and through the
same channel. Activated voice requires the current assistant-name prefix on both
commands, for example `Kora, session permission confirm <session-id>`.
Permission voice commands are unavailable during protected calls; use native
controls instead. Changing channel, policy, lifecycle, private ownership or the
reviewed source requires a fresh review. Cancel discards only the volatile review.
A confirmation attempt consumes it even when the outcome is unconfirmed.

If the current level is already **Approve all**, `session permission review <session-id> <level>` commits the exact change immediately; no separate confirm command is needed. These remain original-user controls, not model tools. Unknown/stale private authority still blocks; no action or provider is enabled ([command owner](../src/Kora.Application/Hosting/SessionPermissionCommandService.cs)).

## What each level means

- **Review all**: otherwise-ungranted actions require your approval.
- **Model review**: an independently qualified, tools-disabled reviewer must
  return a valid Safe verdict for the exact invocation. Otherwise approval is
  required. No production reviewer transport is qualified in this release:
  selecting this level therefore requires user approval, not executor
  self-assessment, a hosted fallback, or a different execution profile.
- **Approve all**: Kora-owned action, script, local MCP process, protected-control, result-continuation and data-disclosure prompts are automatically approved in this session. Existing admission grants are not evaluated or consumed, and no grants are created. Every automatic approval requires a trusted session audit ([policy](../Design/Session_Permission_Levels.md)).

In Review all and Model review, an existing applicable exact grant takes precedence. In Approve all, it is not evaluated. An automatic admission
applies to one invocation only, never another action, retry, session or changed
proposal. Unknown ownership, identity, privacy, containment, authentication,
prohibited effects and unavailable execution remain blocking conditions.

External-page, MCP and hosted Copilot/Claude sends still validate exact destinations/accounts, source/proposal identities and byte bounds. In Approve all, their Kora-owned disclosure cards are skipped, with actual final-request digests audited before every physical send, including follow-ups/retries. Non-sensitive help and deterministic
stop/mute/cancel controls do not acquire an ordinary action-approval step.
Selecting a level enables no scripts, generic MCP transport or model provider.

## Persistence, Done and recovery

In **Settings > Sessions**, choose **Default permission level for new sessions**. The device-local preference initially selects Review all and affects only genuinely new sessions, not existing ones. Chat shows the current session's applied level ([settings](../src/Kora/SettingsView.axaml), [chat controls](../src/Kora/SessionPermission/SessionsViewModel.SessionPermission.cs)).

An explicitly selected policy persists only
for the same Active session across restart. **Done** resets policy in its
lifecycle transaction; **Resume** does not restore the former level. Deletion
removes session policy without deleting independent Always grants.

A policy change retires pending cards, review results and undispatched
admissions from the old revision. Continue explicitly with a freshly resolved
operation; changing a level does not replay cancelled or rejected work.
Corrupt saved policy is an explicit storage error, not a default or approval.
An uncertain write requires current inspection before retry, not a rollback
claim.

Revoke grant is not stop this action. While an automatic level is selected,
a freshly resolved eligible operation can still be admitted without that grant.
Choose Review all to require approval for future ungranted actions; use the
existing cancel/stop controls for owned running work.

## Spoken approvals during calls

Settings / Calls includes **Allow spoken approvals during calls**, Off by
default. Review its device-local change, then confirm it explicitly unless the current admitted session is Approve all; that mode automatically confirms the exact change with a required session audit. Enabling
requires fresh native initiation and a visible downgrade warning. Cancel or
close the settings window to discard the review.

With the setting Off, protected calls reject spoken approval without consuming
a grant or answering the pending card: **Spoken approvals disabled during call;
UI available**. Exact grants and session levels remain unchanged. Native UI
approval still requires independent unlocked ownership and presentation gates.
Call evidence is rechecked inside the exact authorization commit.

Approve all never bypasses microphone consent, memory admission, credential entry, OS/UAC/provider authentication, privileged lifecycle confirmations, hard denials, exact capture/egress enforcement or physical resource joins. Audit failure blocks automatic admission; no raw content is written into automatic approval records ([audit contract](../src/Kora.Core/Auditing/AutomaticApprovalAudit.cs)).

For exactly one live eligible tool-approval card, say **Kora, approve once**,
**Kora, approve for session**, or **Kora, deny**. Kora reads back the host-owned
operation before answering that same card. The actual answer channel is recorded
in the approval audit; the original request origin is unchanged. Typed equivalents
use the same exact-card checks and display the operation review.

No card, multiple eligible cards, expiry, changed authority or failed readback
refuses the reply without selecting another card or sending it to a model.

The same rules apply to running-turn check-ins: say **Kora, keep going**,
**Kora, keep going for 10 minutes**, **Kora, keep going until done** or
**Kora, stop this turn** for exactly one waiting check-in. With this setting Off
during a protected call, use the check-in buttons instead
([check-ins](settings.md#check-ins-while-a-turn-runs)).

Permission cards opened while a turn is running have no time limit. They stay
open until you answer or cancel, or until the request, session or ownership
changes, and the running turn's time limits pause while they are open.
**Always** is not available by voice. Native-only confirmations, including
web/MCP disclosure, MCP process and bundled-script package reviews, stay native-only.

Enabling this setting does not enable listening, new speech-capture surfaces,
authentication, model execution or consent. The existing exact web/MCP cards
remain native-only in Review all/Model review; Approve all skips their Kora-owned prompts, but this setting does not create a spoken-answer route for
them. Automatic call detection remains unavailable where reported. Call
clearance never accepts rejected speech or replays work.
