using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindContentSearch(SessionsViewModel model)
    {
        SearchContent.Click += async (_, _) => await (ConversationOperation = model.SearchContentAsync());
        NextContentSearch.Click += async (_, _) => await (ConversationOperation = model.SearchContentAsync(next: true));
        ClearContentSearch.Click += (_, _) => model.ClearContentSearch();
        ContentQuery.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && model.CanSearchContent)
            {
                args.Handled = true;
                await (ConversationOperation = model.SearchContentAsync());
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                model.ClearContentSearch();
            }
        };
        OpenContentMatch.Click += async (_, _) => await (ConversationOperation = OpenMatch());
        ContentMatches.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter) { return; }
            args.Handled = true;
            await (ConversationOperation = OpenMatch());
        };
        async Task OpenMatch()
        {
            if (ContentMatches.SelectedItem is not SessionsViewModel.ContentMatch match)
            {
                model.ReportNavigationStatus("Select a retained content-search match first. Nothing was opened.");
                return;
            }
            await model.OpenContentMatchAsync(match);
            if (model.SelectedAnswerRecord != match.Message) { return; }
            AnswerSourceRecords.ScrollIntoView(model.SelectedAnswerRecord);
            AnswerSourceRecords.SelectedItem = model.SelectedAnswerRecord;
            AnswerSourceRecords.UpdateLayout();
            if (AnswerSourceRecords.ContainerFromItem(model.SelectedAnswerRecord) is ListBoxItem row
                && row.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text =>
                    string.Equals(text.Name, match.Assistant ? "RetainedAssistantText" : "OriginalMessageText",
                        StringComparison.Ordinal)) is { } body)
            {
                body.BringIntoView();
                if (body.Focus()) { return; }
            }
            model.ReportNavigationStatus("The exact retained message loaded, but native row focus is unavailable. No other message was substituted; retry opening the match.");
        }
    }
}
