# Report an exception on GitHub

Kora does not upload logs or post issues automatically. You need a GitHub
account to post; you can read public issues without one. There is no reporting
service or GitHub credential stored in Kora.

1. Open **Tray > Help > Evidence & diagnostics**, search an available diagnostic
   source and select an exception record.
2. Choose **Report selected exception**. Review the complete report, including
   Kora/Windows versions, architecture, exception type and normalised stack.
3. Choose **Check GitHub using this fingerprint** to approve an external lookup.
   The lookup sends the fingerprint, not the report or raw logs. Offline,
   local-only and unknown network policy deny the operation.
4. If an issue already exists, open it to read its status, workaround and
   fixing-version comments. Closed does not prove a fix. If all matches are
   closed and the failure occurs in a version containing the earlier fix, you
   can explicitly review a possible regression report linking those issues.
5. Otherwise choose **Approve report and review new issue on GitHub**. Kora
   checks again before browser handoff. If matches changed or lookup failed,
   review the updated result rather than creating a duplicate.
6. In your browser, sign in, review or edit the report and select GitHub's
   **Create** button. Opening the browser does **not** post the issue.

The report contains no exception message, raw log properties, conversation
content, paths, local session identifiers or attachments. Stack frames retain
method signatures, not argument values, source filenames or line numbers.
Method/type names may identify installed components: review them before sharing.

The fingerprint is SHA-256 of exception type, a newline and the normalised
stack. Its `v1` marker is included in the issue body. The same signature groups
matching reports, but different signatures can still describe the same bug.
GitHub may suggest additional related issues while you edit the form. Search
index delay and simultaneous submissions mean duplicate prevention is best
effort, not a uniqueness guarantee.

The external browser receives the report in the issue-form URL and may retain
or sync it in browser history. The final public issue can be indexed, copied or
emailed; deleting it cannot recall those copies. Kora cannot close the browser
or retract a handed-off report. Closing the review, changing its selected
source, or losing privacy/ownership admission retires the in-app review.

Old records without a captured stack, missing or excessive managed stacks,
omitted/partial evidence and reports exceeding the browser URL limit remain
unavailable; Kora does not invent or truncate stacks to make them reportable.
Not every fatal/native crash leaves a reportable retained record. Diagnostic
retention remains independent of session history; preparing or viewing a report
does not renew it.

After a published release includes a linked fix, automation comments on the
issue with the version and release link. Beta availability is distinguished
from stable availability. A merged PR alone does not establish that a
downloadable fixing release exists.

For suspected vulnerabilities or exposed credentials, do not publish the
details as a public issue. Use the repository's published security reporting
instructions, if available.
