using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("sessions", true)]
    [InlineData("compact", true)]
    [InlineData("interaction", true)]
    [InlineData("all", true)]
    [InlineData("sessions", false)]
    [InlineData("compact", false)]
    [InlineData("interaction", false)]
    [InlineData("all", false)]
    public async Task ConfirmedUnsentDiscardRetiresReviewEvenAfterIdenticalLegacyRecovery(string surface, bool discard)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            const string body = "  reviewed legacy input e\u0301\r\nexact original  ";
            var legacy = await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(original, f.Token), body);
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new OriginalRequestAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var interaction = new SessionInteractionView { DataContext = model };
            var window = surface switch
            {
                "compact" => new CompactSessionWindow(model, owner),
                "interaction" => (Window)new Window { Content = interaction },
                _ => new SessionsWindow(model, owner)
            };
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                model.ComposerBody = "independent other conversation";
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                await model.RecoverLegacyUnsentAsync();
                await model.ReviewComposerRequestAsync();
                model.CanConfirmComposerRequest.Should().BeTrue(model.ConversationStatus);
                var confirmOldReview = model.CaptureComposerRequestDecision(true);
                var reopenOldReview = model.CaptureComposerRequestReview();
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var retention = await f.Store.ReadRetentionAsync(original, f.Token);
                var decisions = 0;
                model.DecideComposerDraft = () =>
                {
                    decisions++;
                    return Task.FromResult(discard ? ComposerDraftDecision.Discard : ComposerDraftDecision.Cancel);
                };
                if (surface is "all")
                {
                    (await model.ConfirmUnsentDiscardAsync(all: true)).Should().Be(discard);
                }
                else
                {
                    var button = surface is "interaction"
                        ? interaction.FindControl<Button>("DiscardMessage")!
                        : window.FindControl<Button>("DiscardComposer")!;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await (surface switch
                    {
                        "compact" => ((CompactSessionWindow)window).ConversationOperation,
                        "interaction" => interaction.Operation,
                        _ => ((SessionsWindow)window).ConversationOperation
                    });
                }
                decisions.Should().Be(1);
                model.ComposerBody.Should().Be(discard ? string.Empty : body);
                if (discard)
                {
                    await model.RecoverLegacyUnsentAsync();
                    model.ComposerBody.Should().Be(body);
                    model.CanConfirmComposerRequest.Should().BeFalse("discarded input cannot revive its earlier native review");
                    model.CanCancelComposerRequest.Should().BeFalse("confirmed discard releases the old review owner");
                    model.ComposerRequestReviewText.Should().BeEmpty();
                    await confirmOldReview();
                    await reopenOldReview();
                    model.CanConfirmComposerRequest.Should().BeFalse("captured native gestures retain the discarded buffer revision");
                    adapter.Calls.Should().Be(0);
                    f.Count("conversation_messages").Should().Be(0);
                    WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                    (await f.Store.ReadRetentionAsync(original, f.Token)).Should().Be(retention);
                    await model.ReviewComposerRequestAsync();
                    model.CanConfirmComposerRequest.Should().BeTrue(model.ConversationStatus);
                    await model.CaptureComposerRequestDecision(true)();
                }
                else
                {
                    model.CanConfirmComposerRequest.Should().BeTrue("declining discard preserves the original review");
                    await confirmOldReview();
                }
                adapter.Calls.Should().Be(1);
                var message = (await f.Store.ReadConversationPageAsync(original, null, 25, f.Token)).Messages.Single();
                message.Body.Should().Be(body);
                message.AssistantResponse!.Body.Should().Be("Genuine local original-request answer");
                model.ComposerBody.Should().BeEmpty();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                model.ComposerBody.Should().Be(surface is "all" && discard ? string.Empty : "independent other conversation");
                (await f.Store.ReadConversationDraftAsync(original, f.Token)).Should().Be(legacy);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body);
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { owner.Revoke(); window.Close(); }
        });
    }
}
