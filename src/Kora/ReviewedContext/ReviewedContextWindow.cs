using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;

using Kora.Application.Infrastructure;
using Kora.Core.Artifacts;
using Kora.Core.Commands;

namespace Kora;

internal sealed class ReviewedContextWindow : Window
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    internal ReviewedContextWindow(SessionsViewModel model)
    {
        Title = "Review one mixed-context answer";
        Width = 780;
        Height = 780;
        DataContext = model;
        var history = new ListBox
        {
            Name = "MixedHistory", MaxHeight = 140, SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<SessionsViewModel.HistoryContextCandidate>((item, _) =>
                new TextBlock { Text = item is null ? string.Empty : item.Role + "\n" + item.Body, TextWrapping = Avalonia.Media.TextWrapping.Wrap }),
        };
        history.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.MixedHistoryChoices)));
        Avalonia.Automation.AutomationProperties.SetName(history, "Explicit whole earlier USER or ASSISTANT messages");
        history.SelectionChanged += (_, _) =>
            model.SelectMixedHistory(history.SelectedItems?.OfType<SessionsViewModel.HistoryContextCandidate>().ToArray() ?? []);
        var instructions = new ComboBox
        {
            Name = "MixedInstruction", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<ArtifactDefinition>((item, _) => new TextBlock { Text = item?.Name }),
        };
        instructions.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.MixedInstructionChoices)));
        Avalonia.Automation.AutomationProperties.SetName(instructions, "One exact enabled owned instruction");
        instructions.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(model.MixedInstruction)) { Mode = BindingMode.TwoWay });
        var memories = new ListBox
        {
            Name = "MixedMemories", MaxHeight = 140, SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<MemorySummary>((item, _) => new TextBlock
                { Text = item is null ? string.Empty : "Session memory - " + item.CreatedAt.ToString("g", System.Globalization.CultureInfo.CurrentCulture) }),
        };
        memories.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.MixedMemoryChoices)));
        Avalonia.Automation.AutomationProperties.SetName(memories, "Explicit SESSION memory metadata subset; selection does not read content");
        memories.SelectionChanged += (_, _) => model.SelectMixedMemories(memories.SelectedItems?.OfType<MemorySummary>().ToArray() ?? []);
        var input = new TextBox { Name = "MixedRequest", AcceptsReturn = true, IsUndoEnabled = false,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 70 };
        input.Bind(TextBox.TextProperty, new Binding(nameof(model.MixedBody)) { Mode = BindingMode.TwoWay });
        Avalonia.Automation.AutomationProperties.SetName(input, "Unchanged new request for separate one-shot mixed-context confirmation");
        foreach (var control in new Control[] { history, instructions, memories, input })
        { control.Bind(IsEnabledProperty, new Binding(nameof(model.CanEditMixedContext))); }
        var review = new Button { Name = "ReviewMixedContext", Content = "Review complete sources and request" };
        review.Bind(IsEnabledProperty, new Binding(nameof(model.CanReviewMixedContext)));
        review.Command = new AsyncCommand(() => Operation = model.ReviewMixedContextAsync(), model.ReportMixedContextFailure);
        var confirm = new Button { Name = "ConfirmMixedContext", Content = "Confirm ONE local-only tool-free answer" };
        confirm.Bind(IsEnabledProperty, new Binding(nameof(model.CanConfirmMixedContext)));
        confirm.Command = new AsyncCommand(() => Operation = model.ConfirmMixedContextAsync(), model.ReportMixedContextFailure);
        var cancel = new Button { Name = "CancelMixedContext", Content = "Cancel review or use" };
        cancel.Click += (_, _) => model.CancelMixedContext();
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Read the conversation and list session memories in Sessions first, then open this selection. "
            + "Select whole messages, ONE enabled instruction, and admitted SESSION memories. Empty lists require an explicit fresh source read; nothing is recalled automatically.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        foreach (var control in new Control[] { new TextBlock { Text = "Whole earlier USER / ASSISTANT messages" }, history,
            new TextBlock { Text = "Enabled owned instruction" }, instructions, new TextBlock { Text = "Observed SESSION memory metadata" }, memories,
            new TextBlock { Text = "Complete new request (memory-only)" }, input, review, confirm, cancel,
            ReadOnly(nameof(model.MixedReviewText), "Complete mixed-context review"),
            ReadOnly(nameof(model.MixedOutcome), "Genuine answer or explicit HOST failure"),
            ReadOnly(nameof(model.ConversationStatus), "Conversation status") })
        { panel.Children.Add(control); }
        Content = new ScrollViewer { Content = panel };
        var scope = new NameScope();
        NameScope.SetNameScope(this, scope);
        foreach (var control in new Control[] { history, instructions, memories, input, review, confirm, cancel })
        { scope.Register(control.Name!, control); }
        Closed += (_, _) => model.ClearMixedContext();
    }

    private static TextBox ReadOnly(string property, string label)
    {
        var control = new TextBox { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 280 };
        Avalonia.Automation.AutomationProperties.SetName(control, label);
        control.Bind(TextBox.TextProperty, new Binding(property));
        return control;
    }

}
