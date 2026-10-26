# Session action permission levels

Select an existing conversation in the primary Sessions pane or Session
management. **Exact-session action permission level** shows its current policy.
Choose **Read current policy** to refresh an unavailable or stale observation.

1. Select **Review all**, **Model review**, or **Approve all**.
2. Select **Review change**. Check the named session, old/new level and warning.
3. Select **Confirm exact-session change**, or **Cancel review**.

Choosing a level or reading its policy does not change permission, execute an
action, create a grant, or change another session. Confirmation requires a fresh
original native user action and current private desktop ownership. A model/tool
callback or voice request cannot relabel itself as that native action.

## What each level means

- **Review all**: otherwise-ungranted actions require your approval.
- **Model review**: an independently qualified, tools-disabled reviewer must
  return a valid Safe verdict for the exact invocation. Otherwise approval is
  required. No production reviewer transport is qualified in this release:
  selecting this level therefore requires user approval, not executor
  self-assessment, a hosted fallback, or a different execution profile.
- **Approve all**: actions without applicable grants may run without
  discretionary safety review or ordinary approval in this session. No grants
  are saved. Independent consent and mandatory protections still apply.

An existing exact applicable grant takes precedence. An automatic admission
applies to one invocation only, never another action, retry, session or changed
proposal. Unknown ownership, identity, privacy, containment, authentication,
prohibited effects and unavailable execution remain blocking conditions.

The existing external-page and HTTPS MCP workflows still require exact
endpoint/account/tool/argument and disclosure review. Their native consent
cards remain mandatory at every level. Non-sensitive help and deterministic
stop/mute/cancel controls do not acquire an ordinary action-approval step.
Selecting a level enables no scripts, generic MCP transport or model provider.

## Persistence, Done and recovery

New sessions start at Review all. An explicitly selected policy persists only
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
default. Review its device-local change, then confirm it explicitly. Enabling
requires fresh native initiation and a visible downgrade warning. Cancel or
close the settings window to discard the review.

With the setting Off, protected calls reject spoken approval without consuming
a grant or answering the pending card: **Spoken approvals disabled during call;
UI available**. Exact grants and session levels remain unchanged. Native UI
approval still requires independent unlocked ownership and presentation gates.
Call evidence is rechecked inside the exact authorization commit.

Enabling this setting does not enable listening, new speech-capture surfaces,
authentication, model execution or consent. The existing exact web/MCP cards
remain native-only; this setting does not create a spoken-answer route for
them. Automatic call detection remains unavailable where reported. Call
clearance never accepts rejected speech or replays work.
