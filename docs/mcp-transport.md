# Read-only MCP HTTPS transport

**MCP (Model Context Protocol)** connects tools offered by another service.
Kora's first supported connection is read-only and uses HTTPS. It is not a
general remote-command runner.

**Connections are unavailable in this build:** no delegated MCP account is
enabled. Adding a server, enabling hosted models or signing in to another app
does not enable it. The review steps below cannot bypass that restriction.

Kora's reviewed MCP workflow can use a narrow, destination-bound HTTPS
transport. Select an included configured server and an explicitly delegated
account, review the exact connection in native UI, select a host-mapped tool,
and separately approve its exact schema and arguments. The result is inert
lower-trust evidence for the local model, not instructions or permission.
Discovery annotations never establish a read-only mapping.

## Invoke without a model

In Sessions, choose **Review HTTPS MCP options**, then explicitly select the
included server and delegated account. Choose **Invoke reviewed MCP tool without
a model**, or type `invoke selected mcp tool` while that exact session is
foreground. Review the configured server name, configuration source and connection,
select one admitted read-only tool and review
its schema. Enter JSON containing its required bounded inputs. This input step
does not send the tool call: separately approve the final exact destination,
account, tool, schema and arguments.

The original session shows the actual inert tool result or a truthful failure.
No model runs, no assistant answer is invented, and your unsent conversation
text is unchanged. The result remains in memory, not conversation history.
**Stop MCP operation** targets this exact operation and joins its owned transport.
If remote processing is uncertain, the task remains **Unknown**; stopping is
not rollback and does not automatically retry. The availability restriction
below also applies to this route.

A disconnected or failed exchange also joins the original transport without
reconnecting or retrying. Neither route publishes a tool result or assistant
answer from that failure. The direct operation reports failure with an
**Unknown** durable task outcome, not proof that the remote read never occurred.
Inspect the original operation before explicitly starting a newly reviewed one.

## Availability

The shipped host composes the Windows HTTPS broker, but has **no qualified
delegated MCP account installation**. Account selection therefore reports
unavailable before DNS, credential access or HTTP. Including a server does
not enable it. No accounts, credentials, effect mappings or qualification
records are invented from configuration, discovery or another application's
sign-in state.

Kora needs a separately supported account connection bound to the exact
destination and its permission to send information. Changed account or
destination details require new admission. Unknown readiness stays unavailable;
configuration and successful tests are not a real signed-in account.

## Supported profile

The supported protocol version is `2025-06-18`, with one HTTPS request at a
time and complete JSON responses. Server-managed sessions, streaming,
additional result pages, redirects, compressed responses, sign-in retries and
other transports are unsupported. Connection setup and tool discovery accept
at most 16,384 UTF-8 bytes; a tool result accepts at most 1,536. Oversized or
unsupported replies fail rather than being presented as complete results.

All DNS answers must be public. The connection pins the admitted addresses
and connects only to the first one, retaining normal HTTPS hostname/certificate
validation. It never re-resolves while sending. Each POST uses only the
explicit account's bearer credential at its exact endpoint. Proxies, cookies,
ambient credentials, redirects, trace-header forwarding and anonymous fallback
are disabled. No stdio process is started.

Kora rechecks account and transmission permission throughout the connection.
Withdrawal or cancellation stops further use and requests cancellation of the
network operation. Kora waits for the actual operation to finish before
releasing its resources. It cannot promise the remote service stopped or
undid processing already performed.

The host's existing session/privacy, source revision, original request, exact
consent, trusted audit and read-only effect checks still surround the transport.
The broker does not grant tool authority. See
[MCP ownership](../Design/Extensibility.md#mcp-external-system-functionality)
and [identity and credentials](../Design/Security_Data_Flows.md#identity-and-credentials).
