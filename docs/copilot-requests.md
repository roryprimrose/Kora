# GitHub Copilot live evaluation

Kora supports explicit hosted-model evaluation through the pinned Copilot SDK.
This is opt-in evaluation, **not production runtime qualification**. Ordinary
chat submission, local-only source workflows and Claude/Codex are unchanged.
There is no automatic hosted fallback.

## Setup and model selection

1. Install a Copilot runtime separately from a trusted source. Kora does not
   download, bundle or install it. Use a runtime compatible with the
   [pinned SDK](../Directory.Packages.props); compatibility with another installed version is not guaranteed.
2. In **Settings > Providers**, select **Set up Copilot live evaluation and
   account**. Enter the absolute path to the actual installed `copilot.exe`,
   not a shell script, directory or remote executable.
3. Choose **Inspect installed runtime (no execution)**. Review the path and
   SHA-256. This identifies bytes, not trusted provenance. Check the separate
   runtime evaluation/terms/charges consent, then choose **Enable this reviewed
   runtime for live evaluation**.
4. Enter your exact GitHub login and a token supported by the installed
   Copilot runtime for that entitled account. This is a private native token
   channel, not browser/device-code sign-in. Kora never reuses VS Code, `gh`,
   ambient login or environment credentials.
5. Check the independent account-use confirmation, then choose **Verify this
   account and read its model catalogue**. This starts an isolated account
   management runtime, verifies the exact account, fetches its enabled models,
   and stops it. It does not start an inference session.
6. Back in Providers, choose **GitHub Copilot** and use **Refresh** in the
   **Default service and model** actions menu. Select an advertised model and
   its provider-default thinking/conversation-size choices. Choose **Save model
   settings**. Enable **Allow hosted models** separately and choose a provider
   policy that permits hosted use; local-only policy continues to deny it.
7. In **Sessions**, select a conversation, enter a message, and choose
   **Submit explicitly with Copilot (separate disclosure review)**. A session
   override can supersede the app default; select Copilot for that session or
   clear the conflicting override.
   For a tool-free answer with volatile previews, choose **Stream explicitly
   with Copilot (text only, separate disclosure review)** instead. It uses the
   same account, policy and separate per-send disclosure decisions.

## Every send remains a separate decision

Review each exact destination, account/model, noncredential header and complete
request body in the native disclosure window. Approve only the content you
intend to transmit. Declining, closing the review, cancellation or stale
authority denies the send. A subsequent tool-result request or SDK retry needs
new consent; account setup and model selection never approve content egress.

The whole turn, including reviews, has a two-minute deadline. Successful bounded
answers are retained as ASSISTANT data in the same conversation; failed or
denied turns retain no assistant answer. Tool-free requests can show provisional
answer prefixes from the SDK while the same request is running. These previews
are volatile: cancellation or changed authority invalidates them, and only the
validated final answer is retained. Tool-bearing requests remain buffered.
The reviewed HTTP response is still buffered, so this does not promise
network-level token latency. Inherited history, attachments, native Copilot
tools, MCP, skills and agents are not enabled by this operation.

## One selected skill or custom agent

The separate [Customizations operation](../src/Kora/CustomizationWindowController.Copilot.cs) can submit one compatible saved source with a new Copilot request. It does not activate that source globally or change any local-only invocation.

1. In **Customizations**, select the saved skill or agent. Save or reload any unsaved source draft first.
2. Enter the exact name of an existing Active conversation in the selected Copilot target field, and enter the unchanged original task in the request field.
3. Choose **Run selected with Copilot**. Review the source location, primary file and bounded-read disclosure before choosing **Read this exact source**.
4. Inspect the complete source, native selection, unchanged task and exact target. Choose **Authorize this exact hosted copy** only if you intend to disclose this source for this one request.
5. Separately review retention and choose **Retain and submit this one request** once. This retains the original USER task and, later, the genuine ASSISTANT answer or truthful HOST outcome in the reviewed conversation.
6. Review and approve each complete physical transmission in the existing native disclosure window. Source consent and retention confirmation do not approve egress.

The [compatibility reader](../src/Kora.Application/Definitions/CopilotCustomizationSelection.cs) currently accepts one bounded UTF-8 file without a BOM: an instruction-only `SKILL.md` with only name/version/description metadata, or a reference-free, tool-free native v1 agent. Companions, scripts, references, tools, MCP, hooks, imports, model/reasoning overrides and unsupported metadata are refused rather than omitted.

The [native runtime](../src/Kora.Windows/Dependencies/CopilotSdkTurnRuntime.Customizations.cs) selects exactly one custom agent. For a skill, a fixed tool-free host agent preloads only the selected ephemeral provider skill; it does not paste the source into the ordinary user request or enable the native skill tool. No filesystem discovery, inferred agents or continuing selection is enabled.

Choose **Stop selected Copilot request** to request cancellation. [Ownership remains held](../src/Kora.Application/Definitions/CopilotCustomizationWorkflow.cs) until source work, provider callbacks and sends physically join. Cancellation does not claim rollback. Source replacement/removal/disable, changed target, privacy or expired authority prevents further admitted work; inspect the exact conversation after an unconfirmed outcome rather than automatically retrying.

This operation retains the existing [evaluation and qualification limits](#lifetime-privacy-and-limitations). Deterministic native-seam tests do not qualify an installed runtime, account or model.

## Lifetime, privacy and limitations

- Runtime admission is volatile and expires after one hour. The exact reviewed
  executable is held read-only while admitted, preventing replacement/writes.
  Forget, privacy loss, changed private-host admission or restart removes
  admission. Installation updates require a new review.
- The verified account/catalogue expires after at most five minutes. Explicit
  refresh can renew a still-current account. Expired accounts need new native
  setup; refreshed catalogue revisions require reselecting the exact model.
- Tokens are memory-only and cleared from the entry field after submission.
  Managed immutable strings are not a secure-zeroing guarantee.
- Runtime working/state/temp directories are newly allocated beneath Kora's
  device-local root with current-user-only permissions, separate from your
  workspace and ordinary Copilot state. Incidental runtime files may remain
  there; do not treat evaluation as proven no-write containment.
- You must independently accept runtime/account terms, entitlement and possible
  charges. Kora does not enforce a monetary cap. The runtime's all-path network,
  storage and lifecycle qualification remains incomplete; an executable hash
  and fake-backed tests do not close that evidence.
- Missing authentication, catalogue, current admission, compatible runtime,
  hosted policy or native review fails explicitly without substitution or
  automatic installation. Failed process stop remains non-quiescent and blocks
  further use.
