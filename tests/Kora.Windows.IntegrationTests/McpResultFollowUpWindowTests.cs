using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

#pragma warning disable MA0048 // Shares the canonical native fixture in the separately appointed window regression file.
public sealed partial class SessionsConversationTests
#pragma warning restore MA0048
{
    [Theory]
    [InlineData("question")]
    [InlineData("account")]
    [InlineData("server")]
    [InlineData("selection")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("expiry")]
    [InlineData("classification")]
    [InlineData("aba")]
    public async Task McpFollowUp_native_source_review_cannot_survive_changed_question_presentation_or_snapshot(string fault)
    {
        await WithMcpFollowUpAsync(async (f, composed, owner, panel, transport, broker, policy) =>
        {
            var model = composed.Model;
            model.ComposerBody = "untouched ordinary buffer";
            panel.FindControl<TextBox>("McpFollowUpQuestion")!.Text = "Explain this exact observation.";
            Click(panel, "ReviewMcpFollowUp");
            await panel.Operation;
            model.CanConfirmMcpResultFollowUp.Should().BeTrue(model.ConversationStatus);
            switch (fault)
            {
                case "question": panel.FindControl<TextBox>("McpFollowUpQuestion")!.Text = "A different question"; break;
                case "account": model.SelectedMcpAccount = null; break;
                case "server": model.SelectedMcpServer = null; break;
                case "selection": await model.SelectAsync(null); break;
                case "cancel": Click(panel, "CancelMcpFollowUp"); await panel.Operation; break;
                case "close": owner.ClosePrivate(); await model.McpFollowUpRetirement; break;
                case "expiry": f.Time.Now = f.Time.Now.AddSeconds(60); break;
                case "classification": policy.Revision++; break;
                case "aba":
                    await model.InvokeSelectedMcpAsync();
                    model.McpResultText.Should().Contain("reference 42");
                    broker.Calls.Should().Be(2);
                    break;
            }
            if (fault is "expiry" or "classification")
            {
                Click(panel, "ConfirmMcpFollowUp");
                await panel.Operation;
                model.McpFollowUpOutcome.Should().Contain("Unconfirmed or rejected");
            }
            model.CanConfirmMcpResultFollowUp.Should().BeFalse();
            model.McpFollowUpReviewText.Should().BeEmpty();
            transport.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
            if (fault is not ("selection" or "close")) { model.ComposerBody.Should().Be("untouched ordinary buffer"); }
        });
    }

    [Fact]
    public async Task McpFollowUp_native_cancel_physically_joins_ignoring_model_without_a_late_answer()
    {
        await WithMcpFollowUpAsync(async (f, composed, _, panel, transport, _, _) =>
        {
            var model = composed.Model;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.BeforeAnswer = () => { entered.TrySetResult(); return release.Task; };
            panel.FindControl<TextBox>("McpFollowUpQuestion")!.Text = "Explain the observation.";
            Click(panel, "ReviewMcpFollowUp");
            await panel.Operation;
            Click(panel, "ConfirmMcpFollowUp");
            var work = panel.Operation;
            await entered.Task.WaitAsync(f.Token);
            Click(panel, "CancelMcpFollowUp");
            var cancel = panel.Operation;
            model.McpFollowUpReviewText.Should().BeEmpty();
            work.IsCompleted.Should().BeFalse();
            cancel.IsCompleted.Should().BeFalse();
            release.TrySetResult();
            await work;
            await cancel;
            model.McpFollowUpOutcome.Should().Contain("Unconfirmed");
            var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
            retained.AssistantResponse.Should().BeNull();
            retained.ExpectedTaskState.Should().Be(HostTaskState.DispatchRecorded);
            model.CanReviewMcpResultFollowUp.Should().BeFalse("unconfirmed work remains held even after cancellation");
            await model.CancelMcpResultFollowUpAsync();
            model.CanReviewMcpResultFollowUp.Should().BeFalse();
        });
    }
}
