using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [WindowsFact]
    public async Task Native_name_only_confirmation_waits_for_actual_owned_store_erasure()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        const string sentinel = "NATIVE_NAME_ERASURE_7EB864E8";
        await composed.Workspace.RenameAsync(f.Request.SessionId, new(1), 0, new(sentinel), RequestOrigin.LocalUi, f.Token);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.PreviewDispositionAsync();
        model.Detail.Should().Contain(SessionDispositionPreview.Scope)
            .And.Contain("owned SQLite cells/free pages")
            .And.Contain("verified empty before completion")
            .And.Contain("uninventoried copies hold completion");
        System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().Contain(sentinel);
        await model.ConfirmDispositionAsync();
        model.Status.Should().StartWith("Committed logical disposition");
        model.Detail.Should().Be(SessionDispositionPreview.Scope);
        foreach (var path in new[] { f.DatabasePath, f.DatabasePath + "-journal" })
        {
            File.ReadAllBytes(path).AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(sentinel)).Should().Be(-1);
        }
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
        (await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token))!.Generation.Value.Should().Be(2);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ExactCommandDispositionReviewSharesCompleteNativeInventoryAndNeverDisposesOrSubmits(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "explicitly saved private draft");
        var subject = model.SelectedSessionRecord!;
        var before = f.Count("host_tasks");
        var audits = f.Count("security_audit_events");
        var preview = await model.ReviewDispositionAsync(new(subject.Authority.SessionId.Value, subject.Authority.Generation.Value,
            subject.Metadata?.Revision.Value ?? 0), origin, () => true, () => true, f.Token);
        preview.Should().BeTrue();
        model.SelectedSessionRecord.Should().Be(subject);
        model.Detail.Should().Contain(subject.Authority.SessionId.Value.ToString("D"))
            .And.Contain("Generation: " + subject.Authority.Generation.Value)
            .And.Contain("Metadata revision: " + (subject.Metadata?.Revision.Value ?? 0))
            .And.Contain("questions 0").And.Contain("scoped grants 0").And.Contain("observations 1")
            .And.Contain("waits 0").And.Contain("durable file attachments 0")
            .And.Contain("provider overrides 0").And.Contain("conversation messages 0")
            .And.Contain("saved conversation drafts 1").And.Contain(SessionDispositionPreview.Scope);
        model.Status.Should().Contain("nothing removed").And.Contain("separate deliberate action");
        model.CanConfirmDisposition.Should().BeTrue();
        var commandDetail = model.Detail;
        await model.PreviewDispositionAsync();
        model.Detail.Should().Be(commandDetail, "native and command launch disclose the same authoritative inventory");
        f.Count("host_tasks").Should().Be(before);
        f.Count("security_audit_events").Should().Be(audits);
        (await f.Store.ReadMetadataAsync(subject.Authority.SessionId, f.Token)).Should().Be(subject);
        (await f.Store.ReadConversationDraftAsync(subject.Authority.SessionId, f.Token)).Draft.Body
            .Should().Be("explicitly saved private draft");
        f.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("missing")]
    [InlineData("control")]
    [InlineData("native")]
    [InlineData("admission")]
    [InlineData("cancel")]
    [InlineData("closed")]
    public async Task ExactDispositionLaunchNeverFollowsNewerMetadataOrLosesPrivateNativeAdmission(string failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        var target = new SessionDispositionReviewCommand(f.Request.SessionId.Value, 1, 0);
        if (failure is "generation") { target = target with { Generation = 2 }; }
        if (failure is "metadata") { target = target with { MetadataRevision = 1 }; }
        if (failure is "missing") { target = target with { SessionId = Guid.NewGuid() }; }
        if (failure is "control") { composed.Access.CanControl = false; }
        if (failure is "closed") { model.Close(); }
        using var cancellation = new CancellationTokenSource();
        if (failure is "cancel") { cancellation.Cancel(); }
        if (failure is "closed")
        {
            var act = () => model.ReviewDispositionAsync(target, RequestOrigin.LocalUi, () => true, () => true, f.Token);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            (await model.ReviewDispositionAsync(target, RequestOrigin.ActivatedVoice,
                () => failure is not "admission", () => failure is not "native", cancellation.Token)).Should().BeFalse();
        }
        model.CanConfirmDisposition.Should().BeFalse();
        model.Detail.Should().BeEmpty();
        f.Count("host_tasks").Should().Be(1);
        f.Count("conversation_messages").Should().Be(0);
        (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Authority.Generation.Value.Should().Be(1);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("failure")]
    [InlineData("discard")]
    public async Task DispositionLaunchPreservesDirtyComposerUntilASeparateNativeDecision(string choice)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        var target = model.SelectedSessionRecord!;
        model.ComposerBody = "private original unsaved buffer";
        model.DecideComposerDraft = () => choice is "failure"
            ? throw new IOException("synthetic native draft decision failure")
            : Task.FromResult(choice is "discard" ? ComposerDraftDecision.Discard : ComposerDraftDecision.Cancel);
        var shown = await model.ReviewDispositionAsync(new(target.Authority.SessionId.Value, 1, 0),
            RequestOrigin.ActivatedVoice, () => true, () => true, f.Token);
        shown.Should().BeTrue();
        model.SelectedSessionRecord.Should().Be(target);
        model.ComposerBody.Should().Be("private original unsaved buffer");
        (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Revision.Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
        f.Count("host_tasks").Should().Be(1);
    }

    [Fact]
    public async Task NativeOwnerLossAndReplacedPreviewCannotAuthorizeTheOldConfirmation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        var native = true;
        var target = new SessionDispositionReviewCommand(f.Request.SessionId.Value, 1, 0);
        (await model.ReviewDispositionAsync(target, RequestOrigin.ActivatedVoice, () => true, () => native, f.Token)).Should().BeTrue();
        native = false;
        model.CanConfirmDisposition.Should().BeFalse();
        // Exercise the existing deliberate native action only against this owned private fixture.
        await model.ConfirmDispositionAsync();
        model.CanConfirmDisposition.Should().BeFalse();
        f.Count("host_tasks").Should().Be(1);
        (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Authority.Generation.Value.Should().Be(1);
        native = true;
        (await model.ReviewDispositionAsync(target, RequestOrigin.LocalUi, () => true, () => native, f.Token)).Should().BeTrue();
        await f.RunAsync(() => composed.Workspace.PreviewDispositionAsync(f.Request.SessionId, new(1), 0, f.Token));
        model.CanConfirmDisposition.Should().BeFalse();
        await model.ConfirmDispositionAsync();
        f.Count("host_tasks").Should().Be(1);
    }

    [Fact]
    public async Task ReplacingNativeDisclosureWithOtherDetailsRetiresThePendingDispositionPreview()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        (await model.ReviewDispositionAsync(new(f.Request.SessionId.Value, 1, 0),
            RequestOrigin.LocalUi, () => true, () => true, f.Token)).Should().BeTrue();
        model.CanConfirmDisposition.Should().BeTrue();
        await model.ReadHistoryAsync();
        model.CanConfirmDisposition.Should().BeFalse();
        await model.ConfirmDispositionAsync();
        f.Count("host_tasks").Should().Be(1);
    }

}
