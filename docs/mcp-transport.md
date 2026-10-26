# Read-only MCP HTTPS transport

Kora's reviewed MCP workflow can use a narrow, destination-bound HTTPS
transport. Select an included configured server and an explicitly delegated
account, review the exact connection in native UI, select a host-mapped tool,
and separately approve its exact schema and arguments. The result is inert
lower-trust evidence for the local model, not instructions or permission.
Discovery annotations never establish a read-only mapping.

## Availability

The shipped host composes the Windows HTTPS broker, but has **no qualified
delegated MCP account installation**. Account selection therefore reports
unavailable before DNS, credential access or HTTP. Including a server does
not enable it. No accounts, credentials, effect mappings or qualification
records are invented from configuration, discovery or another application's
sign-in state.

A trusted host account adapter must qualify the exact account revision,
protected secret reference, destination, transport profile and egress policy.
It must withdraw its live lease when any of these changes. Unknown readiness
remains unavailable. Deterministic HTTP/account/DNS tests are implementation
evidence, not live service, authentication or installed qualification.

## Supported profile

The first profile uses bounded JSON-RPC `2025-06-18`, single HTTPS POSTs with
JSON responses, and an empty HTTP 202 response for the initialized notification.
It is stateless: server-issued sessions, streaming/SSE, pagination, redirects,
compressed bodies, authentication retries and alternate transports are refused.
Initialization and discovery accept at most 16,384 complete UTF-8 bytes;
invocation results accept at most 1,536. Existing strict schema and inert
text/structured-result validation remains authoritative.

All DNS answers must be public. The connection pins the admitted addresses
and connects only to the first one, retaining normal HTTPS hostname/certificate
validation. It never re-resolves while sending. Each POST uses only the
explicit account's bearer credential at its exact endpoint. Proxies, cookies,
ambient credentials, redirects, trace-header forwarding and anonymous fallback
are disabled. No stdio process is started.

Account/profile/egress currency is checked across DNS, before sending, after
headers and before returning complete bytes. Withdrawal and caller cancellation
cancel the actual HTTP operation. Disposal closes admission, cancels owned work,
joins even late completion and only then disposes the HTTP client and credential
lease. Cancellation is not a claim that the remote service rolled back.

The host's existing session/privacy, source revision, original request, exact
consent, trusted audit and read-only effect checks still surround the transport.
The broker does not grant tool authority. See
[MCP ownership](../Design/Extensibility.md#mcp-external-system-functionality)
and [identity and credentials](../Design/Security_Data_Flows.md#identity-and-credentials).
