using System.Globalization;

using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed partial class NewRequestComposer
{
    private ConversationDraftSnapshot? savedDraft;
    private ConversationDraftSnapshot? saveReview;
    private string? saveBody;
    private string baseline = string.Empty;
    private bool saveHeld;
    private long targetControl;
    private long savedEpoch = -1;

    public bool CanReviewSave => CanReview && access.ControlRevision == targetControl;
    public bool CanSave => CanReviewSave && saveReview is not null;
    public long SavedRevision => savedDraft?.Draft.Revision ?? 0;
    public string BaselineBody => baseline;
    public string SavedBodyReview => saveReview?.Draft.Body ?? string.Empty;
    public string ProposedSavedBody => saveBody ?? string.Empty;
    public bool CanCloseSaved => CanReviewSave && !Dirty && savedDraft is not null && Volatile.Read(ref epoch) == savedEpoch;

    public Task ReviewSave()
    {
        if (!CanReviewSave) { return Task.CompletedTask; }
        RetireReview();
        var exact = target!;
        var frozen = body;
        return Run(async live =>
        {
            var metadata = await workspace.ReadExactMetadataAsync(exact.Authority.SessionId, lifetime.Token);
            var observed = await conversation.ReadDraftAsync(exact.Authority.SessionId, lifetime.Token);
            observed.Draft.Validate();
            var expected = observed with { ExpectedTarget = exact };
            if (!live() || metadata != exact || observed.Session != exact.Authority
                || observed.Draft.SessionId != exact.Authority.SessionId || savedDraft is not null && savedDraft != expected)
            { throw new InvalidOperationException("The original target or known saved revision changed. No newer snapshot is followed."); }
            savedDraft = expected;
            saveReview = expected;
            saveBody = frozen;
            status = "Review Save only: exact saved revision " + SavedRevision.ToString(CultureInfo.InvariantCulture)
                + " and complete old/new bodies below. Confirm Save is separate from Submit; no model request, approval or activity renewal.";
        }, true, saving: true);
    }

    public async Task<bool> Save()
    {
        if (!CanSave) { return false; }
        var expected = saveReview!;
        var frozen = saveBody!;
        var exact = target!;
        RetireReview();
        var confirmed = false;
        await Run(async live =>
        {
            var metadata = await workspace.ReadExactMetadataAsync(exact.Authority.SessionId, lifetime.Token);
            if (!live() || metadata != exact)
            { throw new InvalidOperationException("The original confirmed target changed before Save."); }
            var receipt = await conversation.SaveDraftAsync(expected, frozen, live, lifetime.Token);
            metadata = await workspace.ReadExactMetadataAsync(exact.Authority.SessionId, lifetime.Token);
            if (!live() || metadata != exact || receipt.Session != expected.Session
                || receipt.ExpectedTarget != exact || receipt.Draft.SessionId != expected.Draft.SessionId
                || receipt.Draft.Revision != checked(expected.Draft.Revision + 1)
                || !string.Equals(receipt.Draft.Body, frozen, StringComparison.Ordinal))
            { throw new IOException("Saved draft receipt is unconfirmed. Inspect exact saved revision without retry."); }
            savedDraft = receipt;
            baseline = frozen;
            savedEpoch = Volatile.Read(ref epoch);
            confirmed = true;
            status = "Exact draft saved and acknowledged at revision " + SavedRevision.ToString(CultureInfo.InvariantCulture)
                + ". Body retained in memory; Submit requires its own new review. Saved draft remains independent of submitted USER data.";
        }, true, saving: true);
        return confirmed;
    }
}
