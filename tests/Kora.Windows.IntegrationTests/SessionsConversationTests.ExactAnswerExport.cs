using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Presentation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public async Task NativeExportUsesTheExactEarlierRowAndSeparateOneShotConfirmation(int surface, bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(storage);
            await using var composition = new Composition(storage, adapter: new MemoryAnswerAdapter());
            var model = composition.Model;
            var presentation = new SessionsPresentationOwner(model);
            var primary = new SessionInteractionView { DataContext = model };
            Window owner = surface switch
            {
                1 => new CompactSessionWindow(model, presentation),
                2 => new SessionsWindow(model, presentation),
                _ => new Window { Content = primary, DataContext = model, Width = 1200, Height = 1000 },
            };
            owner.Show();
            var root = Path.Combine(OwnedStorageFixture.ScratchParent(), ".scratch", "orchestration",
                "exact-answer-export", "ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "reviewed.md");
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "first question";
                await model.SubmitConversationAsync();
                model.ComposerBody = "second independent question";
                await model.SubmitConversationAsync();
                await model.ReadConversationAsync();
                var exact = model.ConversationMessages[0];
                var writes = 0;
                model.ExportExactAnswer = async (source, admitted, resolve, token) =>
                {
                    source.ConversationMessage.Should().Be(exact);
                    await using var destination = await new WindowsExactAnswerExportWriter(storage.Paths,
                        NullLogger<WindowsExactAnswerExportWriter>.Instance).SelectAsync(path, token);
                    await using var review = new ExactAnswerExportReview(source, destination, admitted, resolve,
                        composition.Audit, NullLogger<ExactAnswerExportReview>.Instance);
                    await review.ValidateAsync(token);
                    var view = new ExactAnswerExportWindow(review, admitted, _ => throw new InvalidOperationException("Unexpected export failure"), token);
                    view.Show(owner);
                    try
                    {
                        var text = view.GetVisualDescendants().OfType<TextBox>().Single(control =>
                            string.Equals(control.Name, "ExportAnswerSource", StringComparison.Ordinal));
                        text.Text.Should().Be(exact.AssistantResponse!.Body);
                        text.Copy();
                        text.IsReadOnly.Should().BeTrue();
                        File.Exists(path).Should().BeFalse("opening review and selecting a destination never writes");
                        if (cancel)
                        {
                            view.GetVisualDescendants().OfType<Button>().Single(control =>
                                string.Equals(control.Name, "CancelExactAnswerExport", StringComparison.Ordinal))
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            File.Exists(path).Should().BeFalse();
                            return "Export review cancelled. No answer file was published.";
                        }
                        var confirm = view.GetVisualDescendants().OfType<Button>().Single(control =>
                            string.Equals(control.Name, "ConfirmExactAnswerExport", StringComparison.Ordinal));
                        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await view.Operation;
                        writes++;
                        view.Outcome.Should().Be(ExactAnswerExportOutcome.Saved);
                        confirm.IsEnabled.Should().BeFalse();
                        text.Text.Should().BeEmpty();
                        (await File.ReadAllBytesAsync(path, token)).Should().Equal(new System.Text.UTF8Encoding(false, true).GetBytes(source.Source));
                        return ExactAnswerExportWindow.Describe(view.Outcome.Value, path);
                    }
                    finally { view.Close(); await view.Operation; view.Release(); }
                };
                model.ComposerBody = "unsent stays unchanged";
                if (surface != 0) { owner.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(exact); }
                owner.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var button = owner.GetVisualDescendants().OfType<Button>().Single(control =>
                    string.Equals(control.Name, "ExportExactAssistantAnswer", StringComparison.Ordinal)
                    && ReferenceEquals(control.DataContext, exact));
                button.IsEnabled.Should().BeTrue();
                AutomationProperties.GetName(button).Should().Contain("separately confirm");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await (surface == 0 ? primary.Operation : presentation.DetailOperation);
                writes.Should().Be(cancel ? 0 : 1, model.ConversationStatus);
                model.ConversationStatus.Should().Contain(cancel ? "cancelled" : "exact UTF-8 bytes verified");
                model.ComposerBody.Should().Be("unsent stays unchanged");
                (await storage.Store.ReadConversationPageAsync(exact.Request.SessionId, null, 25, storage.Token)).Messages.Should().HaveCount(2);
                storage.Count("scoped_grants").Should().Be(0);
                storage.Count("host_questions").Should().Be(0);
            }
            finally { model.Close(); owner.Close(); Directory.Delete(root, recursive: true); }
        });
    }

    [Theory]
    [InlineData("control")]
    [InlineData("privacy")]
    [InlineData("presenter")]
    [InlineData("foreign")]
    public async Task ExportRejectsLostNativeAuthorityAndForeignRowsBeforeDestinationSelection(string fault)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var composition = new Composition(storage, adapter: new MemoryAnswerAdapter());
        var model = composition.Model;
        try
        {
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "original question";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            var exact = model.ConversationMessages.Single();
            var calls = 0;
            model.ExportExactAnswer = (_, _, _, _) => { calls++; return Task.FromResult("not admitted"); };
            if (fault is "control") { composition.Access.CanControl = false; }
            if (fault is "privacy") { composition.Access.CanInspect = false; composition.Access.ControlRevision++; }
            await model.ExportExactAnswerAsync(fault is "foreign" ? exact with { } : exact, () => fault is not "presenter");
            calls.Should().Be(0);
            (await storage.Store.ReadConversationPageAsync(exact.Request.SessionId, null, 25, storage.Token)).Messages.Should().HaveCount(1);
        }
        finally { model.Close(); }
    }
}
