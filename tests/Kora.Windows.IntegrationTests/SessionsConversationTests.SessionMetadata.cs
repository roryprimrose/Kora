using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task SessionMetadata_both_shared_and_management_buttons_keep_exact_caller_without_composer_or_subject_effects()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(storage);
            await using var composed = new Composition(storage);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var caller = model.SelectedSessionRecord!;
            var controls = new SessionsWindow(model);
            var shared = new SessionInteractionView { DataContext = model };
            var sharedWindow = new Window { Content = shared };
            controls.Show();
            sharedWindow.Show();
            try
            {
                var reviews = 0;
                model.MetadataAnswerRequested = (target, admitted) =>
                {
                    target.Should().Be(caller);
                    admitted().Should().BeTrue();
                    reviews++;
                    return Task.CompletedTask;
                };
                model.ComposerBody = "unrelated unsent composer";
                var retention = await storage.Store.ReadRetentionAsync(caller.Authority.SessionId, storage.Token);
                controls.FindControl<Button>("ReviewSessionMetadata")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controls.ConversationOperation;
                shared.FindControl<Button>("ReviewSessionMetadata")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await shared.Operation;
                reviews.Should().Be(2);
                model.ComposerBody.Should().Be("unrelated unsent composer");
                model.SelectedSessionRecord.Should().Be(caller);
                (await storage.Store.ReadRetentionAsync(caller.Authority.SessionId, storage.Token)).Should().Be(retention);
                storage.Count("conversation_messages").Should().Be(0);
                storage.Count("host_questions").Should().Be(0);
                storage.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                sharedWindow.Close();
                controls.ClosePrivate();
            }
        });
    }

    [Fact]
    public async Task SessionMetadata_real_unqualified_native_composition_retains_unavailable_caller_without_subject_read_or_review()
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var composed = new Composition(storage);
        var caller = await storage.Store.ReadMetadataAsync(storage.Request.SessionId, storage.Token);
        var reviewed = false;
        var receipt = await composed.Conversation.SubmitSessionMetadataAsync(caller, new(Guid.NewGuid()), "Original metadata question",
            Kora.Core.Hosting.RequestOrigin.LocalUi, () => true, (_, _) =>
            {
                reviewed = true;
                throw new InvalidOperationException("Unqualified native host must not inspect the nonexistent subject.");
            }, storage.Token);
        reviewed.Should().BeFalse();
        receipt.Message.Outcome.Should().Be(Kora.Core.Dependencies.ModelTurnOutcome.Unavailable);
        receipt.Message.Request.SessionId.Should().Be(caller.Authority.SessionId);
        receipt.Message.AssistantResponse.Should().BeNull();
        storage.Count("conversation_messages").Should().Be(1);
        storage.Count("scoped_grants").Should().Be(0);
    }
}
