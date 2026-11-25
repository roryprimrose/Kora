using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class CopilotCustomizationWorkflowTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task SeparateSourceAndRetentionDecisionsUseExactNativeSelectionAndRetainOriginalOnce(bool skill, bool sendApproved)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        var sdk = new CopilotSdkTurnRuntimeTests.Fixture(profile: SessionsConversationTests.SelectedCopilotProfile, time: storage.Time);
        await using var runtime = sdk.Runtime;
        await using var composed = new SessionsConversationTests.SelectedCopilotComposition(storage, runtime);
        await using var admission = new InstructionControlAdmission(storage.Store, storage.Store, new(storage.Tasks));
        using var catalogue = Kora.Definitions.Artifacts.EmbeddedArtifactCatalogue.Load();
        var use = new ArtifactUsePreferences(storage.Paths);
        var sources = new SelectedStore(skill);
        var management = new CustomizationManagementService(sources, admission, use, catalogue, composed.Audit);
        await using var workflow = new CopilotCustomizationWorkflow(management, composed.Workspace,
            composed.Conversation, composed.Turns, composed.Audit, storage.Time);
        var target = await composed.Workspace.CreateAsync(new("Selected native target"), RequestOrigin.LocalUi, storage.Token);
        const string original = "  unchanged original\r\ntask  ";
        var finalReviews = 0;
        composed.Reviews.Offered += review =>
        {
            finalReviews++;
            var bytes = Encoding.UTF8.GetString(review.Transmission.Body.AsSpan());
            bytes.Should().Contain("Use brief paragraphs.").And.Contain("unchanged original");
            using var presentation = review.Presentation();
            composed.Reviews.Confirm(review, sendApproved, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        sdk.Client.BeforeSend = async token =>
        {
            var configuration = sdk.Client.Configuration!;
            var agent = configuration.Agents.Definitions!.Single();
            var native = agent.Prompt;
#pragma warning disable MCPSDK001 // Simulates the native SDK's selected agent preloading the single provider skill.
            if (skill)
            {
                var provider = configuration.Skills.Provider!;
                Task<string?> callback;
                using (ExecutionContext.SuppressFlow())
                {
                    callback = Task.Run(async () =>
                    {
                        (await provider.ListSkillsAsync(token)).Should().ContainSingle();
                        return await provider.ReadSkillAsync(agent.Skills!.Single(), token);
                    }, token);
                }
                native = await callback;
            }
#pragma warning restore MCPSDK001
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.githubcopilot.com/chat/completions")
            {
                Headers = { Authorization = new("Bearer", "synthetic") },
                Content = new StringContent(JsonSerializer.Serialize(new { model = SessionsConversationTests.SelectedCopilotProfile.Model.Value, messages = new[] { native, sdk.Client.Prompt } })),
            };
#pragma warning disable MCPSDK001 // Real final-byte boundary, synthetic network transport.
            var handler = (CopilotFinalRequestHandler)sdk.Options!.Models.RequestHandler!;
#pragma warning restore MCPSDK001
            using var response = await handler.SendFinalAsync(request, sdk.Client.SessionId, null, request.RequestUri!.AbsoluteUri, token);
        };
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var review = await workflow.ReviewAsync(sources.Source, target.Metadata!.Name.Value, original,
            RequestOrigin.LocalUi, (text, _) =>
            {
                sources.Reads.Should().Be(0);
                text.Should().Contain(sources.Source.Location).And.Contain(sources.Source.PrimaryFile);
                return Task.FromResult(true);
            }, () => true, storage.Token);
        review.Text.Should().Contain(sources.Snapshot.Files[0].Text).And.Contain(original);
        storage.Count("conversation_messages").Should().Be(0);
        sdk.Network.Sends.Should().Be(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ConfirmAsync(review.Id, () => true, storage.Token));
        storage.Count("conversation_messages").Should().Be(0);
        await workflow.ApproveHostedSourceAsync(review.Id, () => true, storage.Token);
        storage.Count("conversation_messages").Should().Be(0);
        sdk.Network.Sends.Should().Be(0);
        var receipt = await workflow.ConfirmAsync(review.Id, () => true, storage.Token);
        receipt.Message.Request.SessionId.Should().Be(target.Authority.SessionId);
        receipt.Message.Body.Should().Be(original);
        receipt.Message.Outcome.Should().Be(sendApproved ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        (receipt.Message.AssistantResponse?.Body).Should().Be(sendApproved ? "genuine-sdk-boundary-answer" : null);
        storage.Count("conversation_messages").Should().Be(1);
        (await storage.Store.ReadConversationMessageAsync(target.Authority.SessionId, receipt.Message.Request.RequestId, storage.Token))
            .Should().Be(receipt.Message);
        finalReviews.Should().Be(1);
        sdk.Network.Sends.Should().Be(sendApproved ? 1 : 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ConfirmAsync(review.Id, () => true, storage.Token));
        storage.Count("conversation_messages").Should().Be(1);
        composed.Turns.IsQuiescent.Should().BeTrue();
        runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing-presenter")]
    [InlineData("read-declined")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("disable")]
    [InlineData("privacy")]
    [InlineData("target")]
    public async Task MissingConsentOrRetiredExactSourceCannotRetainOrCallRuntime(string fault)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        var sdk = new CopilotSdkTurnRuntimeTests.Fixture(profile: SessionsConversationTests.SelectedCopilotProfile, time: storage.Time);
        await using var runtime = sdk.Runtime;
        await using var composed = new SessionsConversationTests.SelectedCopilotComposition(storage, runtime);
        await using var admission = new InstructionControlAdmission(storage.Store, storage.Store, new(storage.Tasks));
        using var catalogue = Kora.Definitions.Artifacts.EmbeddedArtifactCatalogue.Load();
        var use = new ArtifactUsePreferences(storage.Paths);
        var sources = new SelectedStore(false);
        var management = new CustomizationManagementService(sources, admission, use, catalogue, composed.Audit);
        await using var workflow = new CopilotCustomizationWorkflow(management, composed.Workspace,
            composed.Conversation, composed.Turns, composed.Audit, storage.Time);
        var target = await composed.Workspace.CreateAsync(new("Exact native target"), RequestOrigin.LocalUi, storage.Token);
        var current = true;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        Task<CopilotCustomizationReview> Read() => workflow.ReviewAsync(sources.Source, target.Metadata!.Name.Value,
            "Original request", RequestOrigin.LocalUi, fault is "missing-presenter" ? null
                : (_, _) => Task.FromResult(fault is not "read-declined"), () => current, storage.Token);
        if (fault is "missing-presenter" or "read-declined")
        {
            await Assert.ThrowsAnyAsync<Exception>(Read);
            sources.Reads.Should().Be(0);
        }
        else
        {
            var review = await Read();
            await workflow.ApproveHostedSourceAsync(review.Id, () => current, storage.Token);
            if (fault is "replace") { sources.Snapshot = new(sources.Source, "replacement", sources.Snapshot.Files); }
            if (fault is "remove") { sources.Removed = true; }
            if (fault is "disable") { use.SetDisabled(sources.Source.Id, true); }
            if (fault is "privacy") { current = false; }
            if (fault is "target")
            { await composed.Workspace.RenameAsync(target.Authority.SessionId, target.Authority.Generation, target.Metadata!.Revision.Value, new("Changed target"), RequestOrigin.LocalUi, storage.Token); }
            await Assert.ThrowsAnyAsync<Exception>(() => workflow.ConfirmAsync(review.Id, () => current, storage.Token));
        }
        storage.Count("conversation_messages").Should().Be(0);
        sdk.Network.Sends.Should().Be(0);
        sdk.Options.Should().BeNull();
    }

    private sealed class SelectedStore : ICustomizationStore
    {
        internal SelectedStore(bool skill)
        {
            Source = new("test.reviewed", skill ? CustomizationKind.Skill : CustomizationKind.Agent,
                CustomizationScope.Kora, "reviewed", @"Q:\synthetic\reviewed",
                skill ? "SKILL.md" : "reviewed.agent.md");
            var text = skill ? "---\nname: reviewed\nversion: 1.0.0\ndescription: Reviewed\n---\nUse brief paragraphs."
                : OwnedAgentProfileDocument.ProposeToolFree("reviewed", string.Empty, "Reviewed", "Use brief paragraphs.").Document.Text;
            Snapshot = new(Source, "revision", [new(Source.PrimaryFile, Encoding.UTF8.GetBytes(text))]);
        }
        internal CustomizationSource Source { get; }
        internal CustomizationSnapshot Snapshot { get; set; }
        internal int Reads { get; private set; }
        internal bool Removed { get; set; }
        public InstructionOriginalRegistration? RegisteredInstruction => null;
        public bool IsInstructionRegistered(CustomizationSource source) => false;
        public void WithdrawInstruction(CustomizationSource source) => throw new NotSupportedException();
        public Task<InstructionOriginalRegistration> PrepareInstructionRegistration(CustomizationSource source, CancellationToken token) => throw new NotSupportedException();
        public Task RegisterInstruction(InstructionOriginalRegistration proposal, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomizationSource>> List(CancellationToken token) => Task.FromResult<IReadOnlyList<CustomizationSource>>([Source]);
        public Task<CustomizationSource> Create(CustomizationKind kind, CustomizationScope scope, string name, CancellationToken token) => throw new NotSupportedException();
        public Task<CustomizationSnapshot> Read(CustomizationSource source, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Reads++;
            if (Removed || source != Source) { throw new FileNotFoundException("Selected source removed."); }
            return Task.FromResult(Snapshot);
        }
        public Task<CustomizationSnapshot> ReadRecovery(CustomizationSource source, CancellationToken token) => throw new NotSupportedException();
        public Task<CustomizationSnapshot> Save(CustomizationSource source, CustomizationSnapshot? expected, IReadOnlyList<CustomizationFile> files,
            Func<bool> eligible, Func<Func<CustomizationSnapshot>, Task<CustomizationSnapshot>> commit, CancellationToken token,
            CustomizationSnapshot? copiedFrom = null) => throw new NotSupportedException();
        public Task Delete(CustomizationSnapshot reviewed, Func<bool> eligible, Func<Action, Task> commit, CancellationToken token) => throw new NotSupportedException();
    }
}