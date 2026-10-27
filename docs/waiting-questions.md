# Sessions needing an answer

Type **show sessions needing an answer**, or activate the same request with your
current assistant name, for example **Kora, show sessions needing an answer**.
Kora opens or reuses **All work** and waits for one fresh, bounded **Needs answer**
observation. Opening a window alone does not complete this request.

This explicit request clears earlier lifecycle, name/exact-ID and task-state
filters and starts at the first page. It does not save a filter preference.
The equivalent native operation is **All work**, clear those filters, select
**Needs answer**, then **Refresh**. Ordinary **open all work** still performs no
read; native Refresh and Next remain explicit.

The page shows independently observed waiting groups, unavailable sources,
omitted pending-question counts and continuation status. Select a group for its
body-free details. Each read is limited to ten sessions, fifty task/question
records per session and a complete 64 KiB projection. **OutputBound** means that
a complete group could not fit, not that it has no questions.

**Next** remains available even when a page has zero matching groups. An empty
page, omitted records, unavailable source or end of this observation never
means nobody is waiting or that all work is clear. Changes behind the cursor
require an explicit Refresh; Kora does not poll, retry or renew the read deadline.

Cancel, close, changed input, filters, source or private ownership retire the
page, selection and Find authority. The command still owns and joins an
in-progress storage read even if that reader ignores cancellation, so physical
completion can take longer than the ten-second authority deadline. No late page
becomes current.

This operation does not answer questions, consume reviews, change the voice
reply target, submit unsent conversation text, run or resume tasks, grant
permissions, call a model or renew session activity. Answer a question only in
its separately admitted question interaction.
