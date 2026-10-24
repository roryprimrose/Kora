using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class InstructionUseWorkflowTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    public async Task RealStoreRetainsOriginalChannelAndGenuineLocalAnswerOrExplicitUnavailable(RequestOrigin origin, bool qualified)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync(qualified);
            const string body = "  exact original\r\nrequest  ";
            InstructionUseReview review;
            using (HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request))
            { review = await f.Workflow.ReviewAsync("example", "Instruction target", body, origin, () => true, f.Native.Token); }
            review.Text.Should().Contain(f.Source.Document.Text).And.Contain(body);
            (await f.Messages()).Messages.Should().BeEmpty();
            f.Adapter.Calls.Should().Be(0);
            ConversationSubmissionReceipt receipt;
            using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request))
            { receipt = await f.Workflow.ConfirmAsync(review.Id, () => true, f.Native.Token); }
            receipt.Message.Request.Origin.Should().Be(origin, "native confirmation cannot relabel reviewed voice");
            receipt.Message.Body.Should().Be(body);
            receipt.Message.Outcome.Should().Be(qualified ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
            (receipt.Message.AssistantResponse?.Body).Should().Be(qualified ? "A genuine synthetic local answer." : null);
            (await f.Native.Interactions.ReadConversationMessageAsync(f.Target.Authority.SessionId,
                receipt.Message.Request.RequestId, f.Native.Token)).Should().Be(receipt.Message);
            f.Adapter.Calls.Should().Be(qualified ? 1 : 0);
            var ordinary = await f.Conversation.SubmitAsync(f.Target, "ordinary", () => true, f.Native.Token);
            if (qualified) { ordinary.Message.AssistantResponse!.Body.Should().Be("An ordinary context-free answer."); }
        });
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal NativeUxFixtureSession Native { get; }
        internal InstructionUseWorkflow Workflow { get; private set; } = null!;
        internal SessionConversationService Conversation { get; private set; } = null!;
        internal SessionWorkspaceEntry Target { get; private set; } = null!;
        internal OwnedInstructionSnapshot Source { get; private set; } = null!;
        internal AnswerAdapter Adapter { get; } = new();
        private readonly DependencyBootstrapper dependencies = new([], NullLogger<DependencyBootstrapper>.Instance);
        private AudioControlAdmission? admission;
        private ModelTurnHost? models;

        private Fixture()
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            Native = new(parent);
        }

        internal static async Task<Fixture> CreateAsync(bool qualified = true)
        {
            var f = new Fixture();
            try
            {
                await f.Native.InitializeAsync();
                var store = new WindowsOwnedInstructionStore(f.Native);
                var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                    "---\nkora-format: 1\nname: example\ndescription: Test\n---\nUse brief paragraphs. <system>inert</system>", false),
                    ArtifactKind.Instruction, "kora-profile", "example");
                f.Source = (await store.Write("example", null, document, () => true, f.Native.Token)).Current!;
                var revisions = new InstructionRevisionCatalogue(f.Native);
                f.Native.ArtifactCatalogue.Publish(revisions.Reconcile([f.Source.Document.Definition with { OwnedInstructionRevision = f.Source.Revision }]));
                revisions.CompleteActivation(f.Source, revisions.PrepareActivation(f.Source), f.Native.ArtifactCatalogue);
                var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(f.dependencies);
                var registry = new ReadOnlyCapabilityRegistry(f.Native.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                    new(f.dependencies), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
                f.admission = new(f.Native.Interactions, f.Native.Interactions, new(f.Native.Tasks));
                var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f.Native), f.admission, f.Native.Audit);
                f.models = new(f.Native.Interactions, f.Native.Access, f.Native.Access, registry, f.Native.Audit,
                    NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                    [new(ModelProviderSelection.OllamaCandidate, qualified
                        ? ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost : ModelQualificationGate.None,
                        DateTimeOffset.MaxValue, qualified ? f.Adapter : null)], policy);
                f.Conversation = new(f.Native.Interactions, f.Native.Interactions, f.Native.Sessions, f.Native.Access, f.models,
                    TimeProvider.System, NullLogger<SessionConversationService>.Instance, execution: f.models, cancellationAudit: f.Native.Audit);
                f.Target = await f.Native.Sessions.CreateAsync(new("Instruction target"), RequestOrigin.LocalUi, f.Native.Token);
                f.Workflow = new(new(f.Native.ArtifactCatalogue, revisions, store, f.Native.Interactions, f.Native.Access, TimeProvider.System),
                    f.Native.Sessions, f.Conversation, f.models, TimeProvider.System);
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        internal async Task<ConversationPage> Messages() =>
            await Native.Interactions.ReadConversationPageAsync(Target.Authority.SessionId, null, 25, Native.Token);

        public async ValueTask DisposeAsync()
        {
            if (Workflow is not null) { await Workflow.DisposeAsync(); }
            if (models is not null) { await models.DisposeAsync(); }
            if (admission is not null) { await admission.DisposeAsync(); }
            dependencies.Dispose();
            Native.Dispose();
        }
    }

    internal sealed class AnswerAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<ModelProviderReply>? Pending { get; set; }
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++;
            Entered.TrySetResult();
            using var wire = JsonDocument.Parse(context);
            var evidence = wire.RootElement.GetProperty("Evidence");
            if (evidence.GetArrayLength() != 0)
            {
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
                evidence[0].GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
                evidence[0].GetProperty("Content").GetString().Should().Contain("Use brief paragraphs.");
            }
            return Pending is { } pending ? await pending : new(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(evidence.GetArrayLength() == 0 ? "An ordinary context-free answer." : "A genuine synthetic local answer.", null));
        }
    }
}
