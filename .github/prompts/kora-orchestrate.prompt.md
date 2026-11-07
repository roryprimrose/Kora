---
name: kora-orchestrate
description: 'Bootstrap message-driven Kora orchestration with finite coordinator turns and no scheduled automation.'
agent: Kora Coordinator
---

Apply the [orchestration skill](../skills/kora-orchestrate/SKILL.md).
Bootstrap or resume the registry for this exact coordinator chat. Adopt relevant
owners first, start one bounded planner pass, dispatch eligible reservations,
then finish this bootstrap turn. The persisted workflow remains enabled until
I interrupt it. Child messages drive subsequent finite event turns.

Do not create automation or promise continuous idle resumption without evidence.
Preserve any existing publication restrictions. If none are recorded, establish
ready-PR delivery authority before dispatching publishing workers.
