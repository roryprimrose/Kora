using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Hosting.ReviewedContext;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("input")]
    [InlineData("close")]
    public async Task NativeBudgetRecoveryCompletesOnlyAfterExplicitWholeOmissionFreshReviewAndSeparateConfirmation(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var source = new NativeInstructionSource();
            var snapshot = source.Snapshot!;
            var revisions = new InstructionRevisionCatalogue(f.Paths);
            var catalogue = new ArtifactCatalogue(revisions.Reconcile(
                [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true,
                instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time));
            var model = composition.Model;
            var parent = new SessionsWindow(model);
            parent.Show();
            ReviewedContextWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((candidate, _) =>
            { if (candidate is ReviewedContextWindow mixed) { window = mixed; } });
            try
            {
                await PrepareMemoryContext(model);
                for (var index = 0; index < 8; index++)
                {
                    model.ComposerBody = new string((char)('a' + index), 3800);
                    await model.SubmitConversationAsync();
                }
                await model.ReadConversationAsync();
                var previous = model.ConversationMessages.ToArray();
                previous.Should().HaveCount(8);
                const string question = "  Original \"question\" \u00e9\U0001f600\r\n  ";
                model.ComposerBody = question;
                parent.FindControl<Button>("OpenMixedContext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var native = window!;
                var history = native.FindControl<ListBox>("MixedHistory")!;
                foreach (var row in model.MixedHistoryChoices.Where(row => row.Selection.Role == ConversationHistoryRole.User))
                { history.SelectedItems!.Add(row); }
                var memory = native.FindControl<ListBox>("MixedMemories")!;
                memory.SelectedItems!.Add(model.MixedMemoryChoices.Single());
                var instruction = native.FindControl<ComboBox>("MixedInstruction")!;
                instruction.SelectedItem = model.MixedInstructionChoices.Single();
                var calls = adapter.Calls;
                native.FindControl<Button>("ReviewMixedContext")!.Command!.Execute(null);
                await native.Operation;
                model.MixedReviewText.Should().Contain("OVER BUDGET").And.Contain(question);
                model.CanConfirmMixedContext.Should().BeFalse();
                history.SelectedItems!.Count.Should().Be(8);
                memory.SelectedItems.Count.Should().Be(1);
                instruction.SelectedItem.Should().Be(model.MixedInstructionChoices.Single());
                model.MixedBody.Should().Be(question);
                model.ComposerBody.Should().Be(question);
                f.Count("conversation_messages").Should().Be(8);
                adapter.Calls.Should().Be(calls);
                native.UpdateLayout();
                var omissions = native.FindControl<ItemsControl>("MixedContributions")!;
                var omit = omissions.GetVisualDescendants().OfType<Button>()
                    .First(button => button.Tag is MixedContextContribution { CanOmit: true });
                omit.Command!.Execute(null);
                await native.Operation;
                model.CanConfirmMixedContext.Should().BeTrue(model.ConversationStatus);
                model.MixedReviewText.Should().Contain(question).And.NotContain("OVER BUDGET");
                model.MixedBody.Should().Be(question);
                history.SelectedItems.Count.Should().Be(7);
                adapter.Calls.Should().Be(calls);
                f.Count("conversation_messages").Should().Be(8);
                if (action is "cancel") { native.FindControl<Button>("CancelMixedContext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                if (action is "input") { model.MixedBody += " edited"; }
                if (action is "close") { native.Close(); }
                native.FindControl<Button>("ConfirmMixedContext")!.Command!.Execute(null);
                await native.Operation;
                if (action is "confirm")
                {
                    adapter.Calls.Should().Be(calls + 1);
                    model.MixedOutcome.Should().Be("ASSISTANT\nAnswer from reviewed session fact.");
                    model.ConversationMessages.Single().Body.Should().Be(question);
                    f.Count("conversation_messages").Should().Be(9);
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(question);
                    wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(9);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    native.Close();
                    await model.ReadConversationAsync();
                    model.ConversationMessages.Take(8).Should().Equal(previous);
                }
                else
                {
                    adapter.Calls.Should().Be(calls);
                    f.Count("conversation_messages").Should().Be(8);
                    model.MixedReviewText.Should().BeEmpty();
                    model.MixedContributions.Should().BeEmpty();
                    model.ComposerBody.Should().Be(question);
                }
                f.Count("reviewed_memory").Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
            }
            finally { window?.Close(); parent.ClosePrivate(); }
            model.MixedBody.Should().BeEmpty();
            model.MixedContributions.Should().BeEmpty();
        });
    }
}
