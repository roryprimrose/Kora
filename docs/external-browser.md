# Open an admitted web result in your browser

After explicitly retrieving a web page, choose **Open in external browser** beside
the exact web-result details action. This is not a link embedded in Markdown or
model output: those links remain inert.

Kora opens a separate native review showing the exact validated HTTPS destination
(international host names are displayed in their ASCII form) and the external
browser's privacy limitations. Choose **Open this exact destination once**, or
**Cancel**. Preparing, dismissing or cancelling a review never opens anything.
Confirmation is native-only because it is a separate privacy/disclosure decision.

Only the current admitted, volatile web-result snapshot can supply the destination.
Source replacement, response closure, host/privacy changes, network-mode changes
and a two-minute review expiry invalidate the review. Local-only, unknown and
offline states deny opening without switching modes. An explicitly remote-capable
provider preference is a necessary restriction, not consent: this separate browser
confirmation is still required and grants no model egress.

Windows opens the registered HTTPS browser with only the reviewed URL. Kora does
not invoke a shell command, executable or other URI protocol; read or scrape the
new page; transfer conversation, clipboard or retrieved page text; or enable an
embedded browser. Credentials, fragments, malformed addresses and non-public
literal IP destinations are refused. Browser DNS, redirects and subsequent
navigation are controlled by your external browser, not Kora.

Your browser's identity, signed-in accounts, cookies, permissions and networking
are user/browser-managed, outside Kora's embedded sandbox. The site may receive
IP/device/browser metadata and URL parameters. **Kora cannot stop or close the
external browser or its background requests**, including on Windows lock or
privacy closure.

“Windows accepted” means only that Windows accepted a launch request, not that a
page loaded. A declined or unknown launch is reported honestly, never retried
automatically. Repeated confirmation cannot replay the one-shot review; any new
attempt requires a new explicit review.
