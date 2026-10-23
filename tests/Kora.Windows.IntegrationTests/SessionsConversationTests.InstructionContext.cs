using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeInstructionReviewSeparatelySubmitsActualLocalRetainedAnswerAndNeverChangesDefaultSend()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var source = new NativeInstructionSource();
            var revisions = new InstructionRevisionCatalogue(f.Paths);
            var snapshot = source.Snapshot!;
            var catalogue = new ArtifactCatalogue(revisions.Reconcile(
                [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            await using var subject = new Composition(f, adapter: new InstructionAnswerAdapter(),
                instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time));
            var model = subject.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "Use concise guidance for this answer.";
                window.FindControl<Button>("RefreshInstructions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                source.Reads.Should().Be(0, "candidate enumeration never reads sources");
                window.FindControl<ComboBox>("InstructionSelection")!.SelectedItem = model.InstructionChoices.Single();
                model.CanSubmitInstruction.Should().BeFalse();
                window.FindControl<Button>("ReviewInstruction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.InstructionReviewText.Should().Contain(snapshot.Document.Text).And.Contain(model.ComposerBody);
                model.CanSubmitInstruction.Should().BeTrue();
                f.Count("conversation_messages").Should().Be(0);
                window.FindControl<Button>("SubmitInstruction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.ConversationStatus.Should().Contain("Succeeded");
                var answer = model.ConversationMessages.Single();
                answer.AssistantResponse!.Body.Should().Be("An actual retained local answer.");
                (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, answer.Request.RequestId, f.Token)).Should().Be(answer);
                model.CanSubmitInstruction.Should().BeFalse();
                model.InstructionReviewText.Should().NotContain(snapshot.Document.Text);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                model.ComposerBody = "Next ordinary request";
                await model.SubmitConversationAsync();
                model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be("An ordinary answer without instruction evidence.");
                window.ClosePrivate();
                model.InstructionChoices.Should().BeEmpty();
                model.InstructionChoice.Should().BeNull();
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("input")]
    [InlineData("disable")]
    [InlineData("source")]
    [InlineData("privacy")]
    public async Task NativeInstructionReviewCannotSendAfterItsOriginalInputOrAuthorityChanges(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var source = new NativeInstructionSource();
        var revisions = new InstructionRevisionCatalogue(f.Paths);
        var snapshot = source.Snapshot!;
        var catalogue = new ArtifactCatalogue(revisions.Reconcile(
            [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
        revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
        await using var composed = new Composition(f,
            instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time));
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "Original";
        await model.RefreshInstructionsAsync();
        model.InstructionChoice = model.InstructionChoices.Single();
        await model.ReviewInstructionAsync();
        model.CanSubmitInstruction.Should().BeTrue();
        if (change is "input") { model.ComposerBody = "Changed request"; }
        if (change is "disable") { revisions.DisableExact(snapshot, catalogue); }
        if (change is "source") { source.Snapshot = null; }
        if (change is "privacy") { model.Close(); }
        await model.SubmitInstructionAsync();
        f.Count("conversation_messages").Should().Be(0);
        model.CanSubmitInstruction.Should().BeFalse();
        model.Close();
    }

    private sealed class NativeInstructionSource : IOwnedInstructionStore
    {
        internal int Reads { get; private set; }
        internal OwnedInstructionSnapshot? Snapshot { get; set; } = new("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 1\nname: example\ndescription: Brief style\n---\nUse concise guidance.", false),
                ArtifactKind.Instruction, "kora-profile", "example"));
        public Task<IReadOnlyList<string>> List(CancellationToken token) => throw new NotSupportedException();
        public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token)
        {
            Reads++;
            return Task.FromResult(Snapshot);
        }
        public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? replacement,
            Func<bool> eligible, Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
            throw new NotSupportedException();
    }

    private sealed class InstructionAnswerAdapter : IModelTurnAdapter
    {
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            using var wire = JsonDocument.Parse(context);
            var hasInstruction = wire.RootElement.GetProperty("Evidence").GetArrayLength() != 0;
            if (hasInstruction)
            {
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                wire.RootElement.GetProperty("SystemPolicy").GetString().Should().Contain("lower-trust");
            }
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(hasInstruction ? "An actual retained local answer." : "An ordinary answer without instruction evidence.", null)));
        }
    }
}
