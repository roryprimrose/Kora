using Kora.Core.Auditing;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class EvidenceViewModel
{
    private static bool IsTypedRequest(EvidenceRecord? record) =>
        record is { Host: not null, Audit: not null }
        && (record.Reference.Source == EvidenceSource.Audit
            || record.Reference.Source == EvidenceSource.AuthorityAudit && record.AuthorityProvenance is not null);

    private string DescribeRequest()
    {
        if (selected is null && inspectedRequest is { } subject && page is not null)
        {
            if (page.Records.Count == 0)
            {
                return "Selected request evidence is missing or removed, or its source is unavailable. No pending, terminal success or current execution state is inferred.";
            }
            if (page.Status is not EvidencePageStatus.Available || page.UnavailableSources.Count > 0)
            {
                return "Request evidence unavailable or incomplete. No terminal outcome or current execution state is inferred.";
            }
            var events = page.Records;
            var requested = events.Any(record => record.Audit?.Outcome == SecurityAuditOutcome.Requested
                || record.AuthorityProvenance?.RequestedAudit?.Outcome == SecurityAuditOutcome.Requested);
            var terminals = events.Where(record => record.Audit is { Outcome: not SecurityAuditOutcome.Requested })
                .Select(record => record.Audit!.Outcome.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            return SourceDisclosure(subject) + " "
                + (requested ? "Requested evidence retained. " : "Requested evidence not retained on this page. ")
                + (terminals.Length > 0 ? "Recorded terminal outcome: " + string.Join(", ", terminals) + ". "
                    : "No terminal retained on this page: pending or missing evidence, not proof of running work. ")
                + (page.Cursor is not null ? "Incomplete bounded snapshot; use Next page. " : string.Empty)
                + "Select an event to inspect its exact outcome, reason and retention; related diagnostics are separate, not authority.";
        }
        if (selected is null)
        {
            return "Select a typed audit record, then Read request outcomes. Diagnostic correlation alone is not audit authority.";
        }
        if (selected.Host is null)
        {
            return "Uncorrelated diagnostic: no original host request, parent, authority or outcome is inferred.";
        }
        if (!IsTypedRequest(selected))
        {
            return "Diagnostic correlation only, not a canonical audit outcome. Parent and explicit links are bounded observations; missing evidence is not success.";
        }
        var audit = selected.Audit!;
        var requestedAudit = selected.AuthorityProvenance?.RequestedAudit;
        return SourceDisclosure(selected) + " " + audit.ActionId + ": "
            + (requestedAudit is null ? string.Empty : requestedAudit.Outcome + " -> ")
            + audit.Outcome + (audit.ReasonCode is null ? "." : " (" + audit.ReasonCode + ").")
            + " Retention: " + selected.Retention + ". "
            + (audit.Outcome == SecurityAuditOutcome.Requested
                ? "Terminal not inspected; pending or unavailable evidence is not success. " : string.Empty)
            + "Read request outcomes freshly resolves this exact session, host request and audit correlation, without the search filters. "
            + "Inspect related diagnostics separately to follow recorded causal segments; this never grants execution or approval.";
    }

    private static string SourceDisclosure(EvidenceRecord record) =>
        record.Reference.Source == EvidenceSource.AuthorityAudit
            ? "Canonical committed interaction audit."
            : "Typed diagnostic audit projection; not an atomic interaction receipt.";
}
