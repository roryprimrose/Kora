using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("Cancel")]
    [InlineData("Discard")]
    [InlineData("Save")]
    public async Task PrerequisitePreparationUsesExistingDirtyDraftAdmissionAndNeverSubmits(string choice)
    {
        var decision = Enum.Parse<ComposerDraftDecision>(choice);
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.SelectQueuePrerequisite(model.QueuePrerequisites.Single());
        var prerequisite = model.SelectedQueuePrerequisite;
        model.ComposerBody = "private original buffer";
        model.DecideComposerDraft = () => Task.FromResult(decision);
        var shown = await model.PrepareQueuePrerequisiteAsync(new(f.Request.SessionId.Value, f.Request.TaskId.Value),
            RequestOrigin.LocalUi, () => true, () => true, f.Token);
        shown.Should().Be(decision == ComposerDraftDecision.Discard, model.QueuePrerequisitePreparationStatus);
        model.SelectedQueuePrerequisite.Should().BeSameAs(prerequisite);
        model.ComposerBody.Should().Be(decision == ComposerDraftDecision.Discard ? "" : "private original buffer");
        var saved = await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token);
        saved.Draft.Body.Should().Be(decision == ComposerDraftDecision.Save ? "private original buffer" : null);
        f.Count("conversation_messages").Should().Be(0);
        f.Count("host_tasks").Should().Be(decision == ComposerDraftDecision.Save ? 2 : 1,
            "only the separately clicked existing Save gesture may retain its audited control task; its changed metadata refuses preparation");
        model.CancelPreparedQueuePrerequisite();
        model.SelectedQueuePrerequisite.Should().BeSameAs(prerequisite);
    }
}
