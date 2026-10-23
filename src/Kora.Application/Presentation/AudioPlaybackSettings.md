# Audio playback settings ownership

`AudioPlaybackSettings` owns the volume and Windows-native speech-rate settings
presentation workflow: initial observation, pending selections, choice/status
presentation, configuration subscriptions, and refresh/propose/apply orchestration.
The typed configuration services remain the authority for validation, persistence,
session admission, audit receipts, provider support and output invalidation.
No preference format or qualification gate is duplicated here.

`MainViewModel` delegates bindings and admitted commands to this owner. It still
captures original host, call, privacy, assistant-name, voice-generation and native
surface eligibility, and retains the command's busy state through response cleanup.
It also owns user-visible failure recovery and preservation of interrupted private
responses. The same command methods serve native, typed and activated voice routes;
this extraction adds no route or authority.

Configuration invalidation is forwarded synchronously so an interrupted spoken
response remains visual. Selection updates and binding notifications are dispatched
to the UI. Disposal removes both configuration subscriptions, retires queued
presentation updates and revokes pending operations through their eligibility
predicate. Operations are awaited by their admitted callers; this owner starts no
detached workers and owns no cancellation source. Disposing the owner does not
dispose the shared configuration services or restore held-unavailable output.

The focused presentation tests use real configuration services with in-memory
preferences and deterministic platform/session fakes. Observation-only tests need
no process-wide trace listener. Command tests retain the `Host tracing` collection;
the existing root-view-model characterization tests retain their UI/tracing
serialization and prove the binding, admission and response paths end to end.
Runtime DI tests cover both configured and explicitly unavailable composition.
No installed voice, hardware or provider evidence is required or claimed.
