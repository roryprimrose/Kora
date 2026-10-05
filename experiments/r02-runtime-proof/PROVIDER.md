# Provider, account and cost constraints

Reviewed 2026-10-05. Public documentation is background evidence, not an
account-specific entitlement or legal approval. **No hosted account was
approved or tested.** The user explicitly chose no-account loopback proof
and a draft PR with hosted validation blocked.

## Authentication and permitted use

- Copilot-hosted models require the requesting user's eligible Copilot
  identity/entitlement, organization policies where applicable, and supported
  authentication. Kora must not borrow its developer's account or silently
  reuse ambient CLI/`gh` credentials.
- BYOK uses the selected provider's authentication, endpoint and billing.
  BYOK bypassing Copilot authentication does not waive that provider's terms,
  account limits or destination consent.
- Users authenticate themselves through a supported secure flow. The host
  retains opaque credentials; the model never receives credential values,
  sign-in forms, auth dumps or ordinary-log copies.
- This experiment uses no real key: a distinctive **synthetic** header value
  tests transport placement. The report records only a boolean for header
  presence. Synthetic 401 tests are not proof of successful live sign-in,
  refresh, revocation, MFA, enterprise policy or account isolation.
- Copilot Business/Enterprise purchased directly from GitHub follow GitHub's
  Generative AI Services Terms; Microsoft purchases follow Microsoft Product
  Terms; other Copilot users follow the AI Features section of GitHub's Terms
  of Service. The relevant actual agreement and SDK/embedding use must be
  confirmed for the chosen account, including this non-coding assistant use.
  MIT SDK licensing is not hosted-service licensing.
- No account sharing, resale entitlement, concurrent-user permission or
  unattended service permission is inferred from a functioning SDK.

## Concurrency, quota and rate limits

| Constraint | Evidence / limitation |
|---|---|
| SDK conversations | Actual pinned runtime: two blocked execution conversations plus independently completing management, separate mutable histories and tool exposure. |
| Provider routing | Two separate loopback endpoints; no execution markers/tools in management payload. No live provider identity isolation trial. |
| Kora management limit | One in-flight, 30 remote calls per rolling hour; deterministic exact-boundary tests. This is a host policy, not an upstream allowance. |
| Hosted concurrent requests | BLOCKED. No published fixed numeric limit was established for the intended account/model; independent session APIs do not prove account permission or capacity. |
| Service quotas and throttling | BLOCKED. GitHub documents capacity/high-usage/fairness/abuse rate limits; available credits/budgets depend on plan. Synthetic HTTP 429 fallback is tested, not a real quota. |
| Automatic retries | Three SDK internal retry attempts were observed in the combined 401/429/500 trial despite the error-abort hook. The final host boundary blocked all retries: exactly one forwarded request per management session. Do not rely on the error hook alone. |
| Degradation | Synthetic auth/throttle/server errors, output overflow, deadline and exhausted host budget never authorize a tool or switch providers. Native choices/status must remain deterministic in production. |

No claim is made that 30 calls/hour or the two-execution-plus-manager profile
fits a hosted plan. Select and test the approved model, account tier, region
and concurrency budget before enabling it. Do not probe rate limits by
exhausting a real account without explicit permission.

## Cost assumptions, not a spending authorization

Experiment model charges: **USD 0**. Zero live inference calls, subscriptions
or provisioned resources. Registry installation is a public dependency
download, not model usage.

Current public billing documentation uses AI credits for normal plans
(1 credit = USD 0.01), with model-specific input/cached/output token rates.
Legacy premium-request billing still applies to some annual subscribers;
do not assume either scheme for an uninspected account.

For a proposed model, let `I`, `O`, `C` and `W` be actual billed input,
output (including billable reasoning), cached-input and cache-write tokens,
and let the corresponding published USD-per-million rates be
`rI`, `rO`, `rC`, `rW`:

```text
USD per call = (I*rI + O*rO + C*rC + W*rW) / 1,000,000
Management planning exposure = 30 * USD per call per rolling hour
                             = up to 720 calls/day
                             = up to 21,600 calls in a 30-day month
```

Illustrative assumption only: 8192 billed input tokens and 1024 billed output
tokens, no cache/reasoning/extra calls, at GPT-5 mini's documented
USD 0.25/M input and USD 2.00/M output: USD 0.004096/call,
USD 0.12288/hour, USD 88.4736/30-day month at continuous maximum host rate,
before subscription, tax and execution usage. This model was **not tested
or selected for production**. A 32 KiB/4 KiB byte cap does not prove those
token counts or bound unseen reasoning/provider work. Cancelled calls may
still be billed. This estimate is not a verified worst-case spend ceiling.

A defensible worst case remains BLOCKED until the actual tokenizer, model
limits, account billing, billable reasoning, provider retry behavior and
server-side hard spending controls are observed. Require an explicit
per-trial budget and zero overage setting where supported; ask before
subscription changes, provisioning or any potentially paid inference.

## Public references

SDK documentation was consulted at public revision
`6b4f3a3bde7eb9a8604617b91effe0f4a2e3921b`; installed **1.0.16 typings and
actual observations**, not moving docs alone, determine the candidate's API:

- [SDK authentication](https://github.com/github/copilot-sdk/blob/6b4f3a3bde7eb9a8604617b91effe0f4a2e3921b/docs/auth/README.md).
- [BYOK providers](https://github.com/github/copilot-sdk/blob/6b4f3a3bde7eb9a8604617b91effe0f4a2e3921b/docs/auth/byok.md).
- [Empty mode and per-session isolation](https://github.com/github/copilot-sdk/blob/6b4f3a3bde7eb9a8604617b91effe0f4a2e3921b/docs/setup/multi-tenancy.md).
- [GitHub Copilot terms routing](https://docs.github.com/en/site-policy/github-terms/github-terms-for-additional-products-and-features#github-copilot).
- [Plans](https://docs.github.com/en/copilot/get-started/plans).
- [Usage-based individual billing](https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/billing).
- [Models and pricing](https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing).
- [Rate limits](https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/usage-limits).
- [Legacy request billing](https://docs.github.com/en/copilot/reference/copilot-billing/request-based-billing-legacy/copilot-requests).

Recheck changing service documents at enablement; retain redacted
account-specific entitlement/terms evidence without publishing credentials.
