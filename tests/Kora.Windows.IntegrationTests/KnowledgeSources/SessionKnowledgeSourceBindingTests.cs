using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

public sealed class SessionKnowledgeSourceBindingTests
{
    [Fact]
    public async Task AuditedMetadataSurvivesRestartWithoutRenewingRetentionAndDetachPreservesIndependentData()
    {
        using var f = await Initialize();
        var session = f.Request.SessionId;
        var retention = await f.Store.ReadRetentionAsync(session, f.Token);
        f.Time.Now = f.Time.Now.AddHours(1);
        var source = new KnowledgeSourceReference(Guid.NewGuid(), Guid.NewGuid());
        var binding = await Bind(f, source);
        binding.Source.Should().Be(source);
        binding.StorageRevision.Value.Should().Be(1);
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(retention);
        f.Count("conversation_messages").Should().Be(0);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadBinding(session, f.Token)).Should().Be(binding);
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(retention);
        var audit = WindowsSqliteConversationTests.AuditText(f);
        audit.Should().Contain("session.source.bind").And.NotContain(source.SourceId.ToString());
        await Detach(f, binding);
        (await f.Store.ReadBinding(session, f.Token)).Should().BeNull();
        (await f.Store.ReadMetadataAsync(session, f.Token)).Authority.IsActive.Should().BeTrue();
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(retention);
        f.Count("session_knowledge_source").Should().Be(1, "an audited tombstone prevents association resurrection");
        var replacement = await Bind(f, new(Guid.NewGuid(), Guid.NewGuid()));
        replacement.StorageRevision.Value.Should().Be(3);
        replacement.BindingId.Should().NotBe(binding.BindingId);
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("generation")]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("admission")]
    public async Task UnknownStaleOrNonOriginalAuthorityCannotBind(string fault)
    {
        using var f = await Initialize();
        var original = fault is "occupied" ? await Bind(f, new(Guid.NewGuid(), Guid.NewGuid())) : null;
        var request = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()),
            fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            fault is "invocation" ? new(Guid.NewGuid()) : null);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(request, f.Token);
        var auditCount = f.Count("security_audit_events");
        await FluentActions.Awaiting(() => f.Store.Bind(request, new(fault is "generation" ? 2 : 1),
            new(Guid.NewGuid(), Guid.NewGuid()), 1, () => fault is not "admission", f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
        f.Count("security_audit_events").Should().Be(auditCount);
        (await f.Store.ReadBinding(f.Request.SessionId, f.Token)).Should().Be(original);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("missing-row")]
    [InlineData("audit")]
    [InlineData("revision")]
    public async Task CorruptOrUnprovenAssociationNeverReopensAsAuthority(string fault)
    {
        using var f = await Initialize();
        await Bind(f, new(Guid.NewGuid(), Guid.NewGuid()));
        f.Mutate(fault switch
        {
            "payload" => "UPDATE session_knowledge_source SET payload='null';",
            "missing-row" => "DELETE FROM session_knowledge_source;",
            "audit" => "UPDATE session_knowledge_source SET audit_sequence=1;",
            _ => "UPDATE session_knowledge_source SET revision=revision+1;",
        });
        await FluentActions.Awaiting(() => f.Store.ReadBinding(f.Request.SessionId, f.Token).AsTask())
            .Should().ThrowAsync<InvalidDataException>();
        f.Reopen();
        await FluentActions.Awaiting(() => f.Store.InitializeAsync(f.Token).AsTask())
            .Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Version16MigratesEmptyMetadataAndCommittedAssociationsCannotBeDowngradedAway()
    {
        using var f = await Initialize();
        f.Mutate("DROP TABLE session_knowledge_source; PRAGMA user_version=16;");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadBinding(f.Request.SessionId, f.Token)).Should().BeNull();
        await Bind(f, new(Guid.NewGuid(), Guid.NewGuid()));
        f.Invoking(value => value.DropEmptyModelUsageSchemaForLegacy()).Should().Throw<InvalidOperationException>();
        f.Mutate("DROP TABLE session_knowledge_source; PRAGMA user_version=16;");
        f.Reopen();
        await FluentActions.Awaiting(() => f.Store.InitializeAsync(f.Token).AsTask())
            .Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ReviewedSessionDeletionInventoriesAndInvalidatesOnlyAssociationMetadata()
    {
        using var f = await Initialize();
        await Bind(f, new(Guid.NewGuid(), Guid.NewGuid()));
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var preview = await f.Store.PreviewDispositionAsync(f.Request.SessionId, target.Authority.Generation, 0, f.Token);
        preview.KnowledgeSourceBindings.Should().Be(1);
        SessionDispositionPreview.Scope.Should().Contain("association");
        var request = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(request, f.Token);
        await f.Store.DisposeSessionAsync(request, preview, () => true, f.Token);
        f.Count("session_knowledge_source").Should().Be(1);
        (await f.Store.ReadHistoryAsync(request.SessionId, null, 50, f.Token)).Disposed.Should().BeTrue();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
    }

    private static async Task<InteractionStorageFixture> Initialize()
    {
        var f = new InteractionStorageFixture();
        try
        {
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static async Task<SessionKnowledgeSourceBinding> Bind(InteractionStorageFixture f, KnowledgeSourceReference source)
    {
        var request = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(request, f.Token);
        return await f.Store.Bind(request, new(1), source, 1, () => true, f.Token);
    }

    private static async Task Detach(InteractionStorageFixture f, SessionKnowledgeSourceBinding binding)
    {
        var request = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(request, f.Token);
        await f.Store.Detach(request, binding, () => true, f.Token);
    }
}
