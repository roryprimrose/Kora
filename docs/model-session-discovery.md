# Reviewed model session discovery

Kora has a bounded implementation for sharing **reviewed Active-session metadata**
with one explicitly requested local model turn. It does not read conversation
messages or choose a conversation for you.

## Availability

This is an implementation boundary, not an enabled provider profile or a new
desktop command. Production model tool-loop qualification is still required;
unknown or unqualified runtime readiness remains unavailable. The ordinary
six-tool read-only catalogue and tool-free requests do not gain session access.
No management/routing inference is enabled by this operation.

## Exact review and result

The host operation requires original local UI or activated-voice input attached
to an Active session and a live, nonterminal task. An invocation, retained answer,
model suggestion or tool response cannot supply that authority.

1. The host reads one bounded Active-session metadata source page.
2. Before any model send, the trusted review presenter shows the original request,
   every exact session ID and name to be shared (including unnamed sessions), and
   the disclosure scope. The presenter must collect explicit Confirm or Cancel
   from the original user, not from model output.
3. Confirm permits only that page in that original local turn. Cancel shares
   nothing with the model. A subsequent source page requires a new review.
4. The model may call `sessions.list` with optional integer `offset` and `count`.
   Pagination is within the reviewed page, not access to additional sessions.
   Names and IDs are lower-trust data; they cannot grant authority or instruct
   Kora to navigate, route or act.

The default review has at most six records. The host may request fewer when
necessary to fit the complete byte budget. Source metadata order is canonical
session ID order, not relevance. A result contains only `sessionId`, an optional
`name`, `totalRecords`, and an optional `nextOffset` within that review. An empty
review yields an empty result, not a fabricated match.

For example, after a review containing exactly this one session, input
`{"offset":0,"count":1}` returns this complete capability envelope:

```json
{"outcome":"Succeeded","reason":"reviewed-active-metadata-lower-trust","sessions":{"records":[{"sessionId":"10000000-0000-0000-0000-000000000001","name":"Project planning"}],"totalRecords":1}}
```

Inputs are bounded to 1,024 UTF-8 bytes. Each complete capability result is
bounded to 4,096 UTF-8 bytes, including JSON escaping and framing. The existing
turn-wide cumulative byte/call/deadline bounds still apply and may prevent a
large page from being continued. No partial name, silent omission, truncation,
retry or alternate-provider fallback repairs an oversized result.

## Freshness, privacy and exclusions

The host rechecks original task/session, private desktop ownership, control
revision, source generation and exact name revision before disclosure and model
continuation. Rename/lifecycle/retirement notifications invalidate the review;
metadata is reread through the same workspace service. These are mutable
observations, not a historical or cross-process atomic inventory. Lifecycle
changes, cancellation, a completed caller or revoked privacy suppress late
results. A cancelled transport retains its host slot until actual termination.

This operation does not read transcripts, summaries, artifacts, due dates,
activity, task status, grants, credentials or other session content. It does not
refresh retention, resume/select/navigate sessions, choose routing, transfer a
grant, invoke another tool, authorize hosted disclosure or enable a provider.
Audit records contain host-correlated operation outcomes, not session names or
request/response content.

Related: [Sessions](../Design/Interaction_And_Sessions.md), [internal model tools](../Design/Internal_Model_Tools.md#reviewed-active-session-discovery---2026-10-26).
