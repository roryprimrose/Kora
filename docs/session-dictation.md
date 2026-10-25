# Dictate an unsent session message

Unsent messages are separate, **memory-only** buffers for each session. Dictation
does not save a draft, create a conversation, answer a question, approve work or
send a message.

## Availability

Literal dictation is currently **unavailable in production Windows capture**.
An installed speech recognizer does not qualify literal transcription. Opening
the dictation window or holding its button cannot enable listening, grant
microphone consent, select a device or configure a provider. Ordinary typing
and separate submission remain available.

## Explicit interaction

When a qualified capture implementation and current consented push-to-talk
readiness are available:

1. Select the exact **Active** session. Its own unsent input is the only target.
2. Choose **Dictate session message...** in Sessions, the compact conversation,
   or the expanded main conversation surface. The separate native window
   identifies the selected conversation and session.
3. Hold **Hold to dictate** with the pointer, or focus it and hold Space or Enter.
   A normal button click/automation Invoke is not a capture gesture.
4. Release the pointer/key. Kora must confirm actual microphone release before
   exposing a review or inserting any literal text.
5. For nonempty input, inspect the complete original message and literal
   transcription, then choose **Append**, **Replace** or **Cancel dictation**.
   Append concatenates exactly, without adding a space or newline. An empty
   buffer receives the literal text after microphone release.
6. Return to the ordinary conversation composer, inspect/edit the resulting
   unsent message, and use the ordinary **Submit** action separately if desired.
   Submission retains its own validation and permission rules.

Words such as "approve", "submit" or "stop listening" in the transcription are
inert text, not commands or approval. **Cancel dictation** or Escape discards the
pending transcription without changing the original input. Losing the native
arm, changing the session/source, editing even to identical text, closing the
surface, locking, privacy changes or withdrawing control retire stale work.
Switching sessions preserves each session's independent unsent input; text is
never transferred to the newly selected session.

Cancellation requests and actual resource release are different. The private
window hides on close, but Kora continues joining owned native work before
allowing another capture. An unconfirmed release stays unavailable, with an
explicit recovery status rather than a successful fallback.
Closing Kora does not introduce disk autosave or change existing unsent-discard and legacy
read-only recovery behavior.
