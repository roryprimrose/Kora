using Kora.Application.Voice;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class NewRequestComposer
{
    internal ComposerDictationTarget CaptureDictationTarget()
    {
        var revision = DictationRevision;
        var control = DictationControlRevision;
        return new(this, Body, HostRequest.Create(RequestOrigin.LocalUi),
            () => AdmitsDictation(revision, control),
            (literal, admitted) => InsertDictation(revision, control, literal, admitted));
    }

    internal long DictationRevision => Volatile.Read(ref epoch);
    internal long DictationControlRevision => access.ControlRevision;

    internal bool AdmitsDictation(long revision, long control) =>
        CanEdit && !Busy && access.CanControl && DictationRevision == revision && access.ControlRevision == control;

    internal bool InsertDictation(long revision, long control, string literal, Func<bool> admitted)
    {
        if (!AdmitsDictation(revision, control) || !admitted()) { return false; }
        var previous = body;
        RetireReview();
        var inserted = DictationRevision;
        body = literal;
        status = "Literal dictation inserted into private memory only. No command, answer, approval, Create, Save or Submit.";
        var completed = false;
        try
        {
            Notify();
            completed = AdmitsDictation(inserted, control) && admitted();
            return completed;
        }
        finally
        {
            if (!completed)
            {
                body = closed || !access.CanInspect ? string.Empty
                    : DictationRevision != inserted && !string.Equals(body, literal, StringComparison.Ordinal) ? body : previous;
                RetireReview();
                status = "Dictation publication revoked. No dictation handoff completed; newer independent edits are preserved.";
                Notify();
            }
        }
    }
}
