using AwesomeAssertions;
using Kora.Application.Agents;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Skills;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("no-resolver")]
    [InlineData("wrong-review")]
    [InlineData("nonoriginal")]
    [InlineData("target")]
    [InlineData("source")]
    [InlineData("input")]
    [InlineData("expired")]
    [InlineData("oversized")]
    [InlineData("aggregate")]
    [InlineData("repeated-declaration")]
    [InlineData("agent-disabled")]
    [InlineData("agent-reenabled")]
    public async Task SharedSkillAgentPortableWorkflowPreservesDistinctConsentsAndRetainedOperation(string scenario)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = new SessionCommandStore(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var model = new SpeechSuggestionModel();
        var conversation = new SessionConversationService(store, store, workspace, store, model, TimeProvider.System,
            NullLogger<SessionConversationService>.Instance, execution: model);
        await using var shared = new SharedSkillAgentGuidanceLeaseTests.Fixture();
        await shared.Register();
        var resolver = new AgentSkillReferenceResolver(shared.Discovery, shared.Workflow);
        var reader = new ReferencedProfileReader();
        reader.Text = reader.Text.Replace("kora-agent-format: 2", "kora-agent-format: 3", StringComparison.Ordinal)
            .Replace("references: disk.kora-profile.instruction.example", "references: " + shared.Reader.Snapshot().SourceQualifiedIdentity, StringComparison.Ordinal);
        if (scenario is "oversized")
        { reader.Text = reader.Text.Replace("maximum-input-bytes: 32768", "maximum-input-bytes: 100", StringComparison.Ordinal); }
        if (scenario is "aggregate")
        {
            reader.Text += "\n" + new string('a', 40 * 1024);
            shared.Reader.Primary += "\n" + new string('b', 24 * 1024);
        }
        var usePolicy = new Kora.Application.Configuration.ArtifactUsePreferences(
            new Kora.Application.UnitTests.Configuration.LocalSharedSkillPreferencesTests.Store());
        var agentIdentity = ArtifactUseIdentity.File(reader.Metadata.CanonicalPath);
        await using var workflow = new BoundedAgentWorkflow(reader, store, store, conversation, model, f.Audit,
            TimeProvider.System, usePolicy: usePolicy, skills: scenario is "no-resolver" ? null : resolver);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        if (scenario is "no-resolver")
        {
            await workflow.Invoking(value => value.ReadSkillSourcesAsync(() => true, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            await workflow.Invoking(value => value.ReadSkillCatalogueAsync(shared.Reader.Source, () => true, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            await workflow.Invoking(value => value.ReviewAsync("role.agent.md", store.Session, "original", RequestOrigin.LocalUi,
                () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            reader.Released.Should().BeTrue();
            return;
        }
        (await workflow.ReadSkillSourcesAsync(() => true, TestContext.Current.CancellationToken)).Should().Equal(shared.Reader.Source);
        (await workflow.ReadSkillCatalogueAsync(shared.Reader.Source, () => true, TestContext.Current.CancellationToken)).Packages.Should().HaveCount(1);
        if (scenario is "agent-disabled")
        {
            usePolicy.SetDisabled(agentIdentity, true);
            await workflow.Invoking(value => value.ReviewAsync("role.agent.md", store.Session, "original",
                RequestOrigin.LocalUi, () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            reader.Released.Should().BeTrue();
            model.Calls.Should().Be(0);
            store.RequestRetentions.Should().Be(0);
            return;
        }
        var current = true;
        using var review = await workflow.ReviewAsync("role.agent.md", store.Session, "original",
            RequestOrigin.LocalUi, () => current, TestContext.Current.CancellationToken);
        var revision = review.SourceRevision;
        review.Profile.References.Should().BeEmpty();
        review.IsInvocationReady.Should().BeFalse();
        if (scenario is "wrong-review")
        {
            await using var foreign = new BoundedAgentWorkflow(new ReferencedProfileReader(), store, store, conversation, model, f.Audit,
                TimeProvider.System, skills: resolver);
            await foreign.Invoking(value => value.EnableSkillAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            return;
        }
        if (scenario is "nonoriginal")
        {
            await workflow.Invoking(value => value.ReviewSkillDeclarationAsync(review, shared.Reader.Source, shared.Reader.Snapshot(),
                RequestOrigin.HostSystem, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            shared.Reader.Reads.Should().Be(0);
            return;
        }
        await workflow.ReviewSkillDeclarationAsync(review, shared.Reader.Source, shared.Reader.Snapshot(), RequestOrigin.LocalUi,
            TestContext.Current.CancellationToken);
        shared.Reader.Reads.Should().Be(0);
        if (scenario is "repeated-declaration")
        {
            await workflow.Invoking(value => value.ReviewSkillDeclarationAsync(review, shared.Reader.Source, shared.Reader.Snapshot(),
                RequestOrigin.LocalUi, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            reader.Released.Should().BeTrue();
            return;
        }
        if (scenario is "target") { shared.Reader.OnRead = store.ChangeAgentTarget; }
        if (scenario is "target" or "oversized" or "aggregate")
        {
            await workflow.Invoking(value => value.ConsentToSkillReferencesAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<Exception>();
            reader.Released.Should().BeTrue();
            model.Calls.Should().Be(0);
            store.RequestRetentions.Should().Be(0);
            return;
        }
        await workflow.ConsentToSkillReferencesAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken);
        review.SourceRevision.Should().NotBe(revision);
        review.IsInvocationReady.Should().BeFalse();
        await workflow.EnableSkillAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken);
        review.IsInvocationReady.Should().BeTrue();
        model.Calls.Should().Be(0);
        store.RequestRetentions.Should().Be(0);
        if (scenario is "source") { shared.Reader.Primary += "\nChanged"; }
        if (scenario is "input") { current = false; }
        if (scenario is "expired") { shared.Time.Now = shared.Time.Now.AddMinutes(3); }
        if (scenario is "agent-reenabled")
        {
            usePolicy.SetDisabled(agentIdentity, true);
            usePolicy.SetDisabled(agentIdentity, false);
        }
        if (scenario is not "success")
        {
            await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<Exception>();
            model.Calls.Should().Be(0);
            store.RequestRetentions.Should().Be(0);
            return;
        }
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken);
        receipt.Message.Body.Should().Be("original");
        receipt.Message.AssistantResponse!.Body.Should().Be("set speech.summary-words to 40");
        reader.Released.Should().BeTrue();
        model.Calls.Should().Be(1);
        store.RequestRetentions.Should().Be(1);
    }
}
