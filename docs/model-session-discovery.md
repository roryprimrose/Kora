# Reviewed model session discovery

This page explains a restricted tool for sharing **reviewed Active conversation
names and identifiers** with one local AI request. It does not share conversation
messages or choose a destination for you. To find a conversation yourself,
use [the session list](windows-and-tray.md#search-the-session-list).

## Availability

**This is unavailable in this build, not a desktop command to try.** No local
model is approved for release use. Ordinary model requests and other
read-only tools do not gain access to session names through this feature.
It does not enable session management or automatic destination selection.

## Exact review and result

The operation requires your original on-screen or activated-voice request in
an Active session with ongoing work. A saved answer, model suggestion or tool
reply cannot grant that permission.

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

The default review contains at most six records, possibly fewer to fit the
size limit. They are ordered by identifier, not relevance. Only the reviewed
names, identifiers, record count and within-review continuation are available;
an empty review never becomes a guessed match. Additional sessions need a
new review. Complete tool inputs are limited to 1,024 UTF-8 bytes and results
to 4,096, alongside the request's overall time and size limits. Oversized
results are refused, not silently shortened or sent through another provider.

## Freshness, privacy and exclusions

Renaming a conversation, changing its state, cancelling or losing private
access invalidates the review. Kora checks again before sharing and before
continuing an answer. These are current observations, not proof of a complete
inventory. Cancelled work still holds its resources until it actually stops;
late results cannot revive permission.

This operation does not read transcripts, summaries, artifacts, due dates,
activity, task status, grants, credentials or other session content. It does not
refresh retention, resume/select/navigate sessions, choose routing, transfer a
grant, invoke another tool, authorize hosted disclosure or enable a provider.
Audit records contain host-correlated operation outcomes, not session names or
request/response content.

Related: [request destinations](session-routing.md) and
[reviewed conversation context](reviewed-context.md).
