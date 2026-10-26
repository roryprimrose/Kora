using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Agents;

namespace Kora;

internal sealed class AgentProfileEditorWindow : Window
{
    internal TextBox NameInput { get; } = new() { PlaceholderText = "new-lowercase-owned-id", IsUndoEnabled = false };
    internal TextBox Source { get; } = new() { AcceptsReturn = true, Height = 250, IsUndoEnabled = false };
    internal ComboBox Inventory { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBox Review { get; } = new() { AcceptsReturn = true, Height = 250, IsReadOnly = true, IsUndoEnabled = false };
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal Button Confirm { get; } = new() { Content = "Confirm Save as new...", IsEnabled = false };
    private Window? dialog;

    internal AgentProfileEditorWindow(Action<string> run)
    {
        Title = "New Kora-owned agent profile";
        Width = 900; Height = 900;
        var panel = new StackPanel { Margin = new(20), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "Author a new inert local text profile: tool-free v1, or v4 declaring exact owned instruction IDs and existing observation tools. "
                + "Saving never resolves, enables or invokes references or grants tool authority. "
                + "Only new files under the private owned-agent directory; existing originals reopen read-only.",
            TextWrapping = TextWrapping.Wrap,
        });
        AutomationProperties.SetName(NameInput, "New owned agent ID");
        AutomationProperties.SetName(Source, "Complete original agent source");
        AutomationProperties.SetName(Review, "Complete exact new-file review");
        AutomationProperties.SetName(Inventory, "Owned inert agent originals");
        panel.Children.Add(NameInput);
        Add("Create new volatile draft", "Create");
        panel.Children.Add(Source);
        Add("Validate complete v1 or v4 source", "Validate");
        Add("Review Save as new", "Review");
        panel.Children.Add(Review);
        panel.Children.Add(Confirm);
        Confirm.Click += (_, _) => run("Confirm");
        Add("Cancel save review", "Cancel");
        Add("Refresh owned inventory", "List");
        panel.Children.Add(Inventory);
        Add("Reopen selected original read-only", "Inspect");
        panel.Children.Add(Status);
        Content = new ScrollViewer { Content = panel };
        void Add(string label, string action)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => run(action);
            panel.Children.Add(button);
        }
    }

    internal void ShowReview(AgentProfileSaveReview review)
    {
        var profile = review.Document.Profile;
        Review.Text = $"NEW ORIGINAL: {review.Destination.DisplayPath}\n"
            + "CREATE ONLY; NO REPLACEMENT. Local text -> text; references are unresolved declarations, not enabled guidance.\n"
            + $"Declared references: {review.Document.Document.Metadata["references"]}\n"
            + $"Declared tools: {review.Document.Document.Metadata["tools"]}\n"
            + $"Host clamps: calls {profile.MaximumCalls}, rounds {profile.MaximumRounds}, input {profile.MaximumInputUtf8Bytes} bytes, "
            + $"output {profile.MaximumOutputUtf8Bytes} bytes, deadline {profile.Deadline.TotalMilliseconds}ms.\n"
            + $"Complete proposed UTF-8 source ({review.Document.Document.ByteCount} bytes, no BOM):\n\n"
            + review.Document.Document.Text
            + "\n\nSaving does not select, enable or invoke this profile. Separate native confirmation is required.";
        Confirm.IsEnabled = true;
    }

    internal async Task<int> Choose(string title, string message, string[] choices, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Choose(title, message, choices, token)); }
        if (!IsVisible) { throw new InvalidOperationException("The private authoring window is closed."); }
        if (dialog is not null) { throw new InvalidOperationException("A native authoring decision is already open."); }
        var choice = new Window { Title = title, Width = 800, Height = 650 };
        dialog = choice;
        var selected = -1;
        var panel = new StackPanel { Margin = new(20), Spacing = 12 };
        panel.Children.Add(new TextBox { Text = message, AcceptsReturn = true, IsReadOnly = true, IsUndoEnabled = false, Height = 450 });
        for (var index = 0; index < choices.Length; index++)
        {
            var value = index;
            var button = new Button { Content = choices[index], IsDefault = false };
            button.Click += (_, _) => { selected = value; choice.Close(); };
            panel.Children.Add(button);
        }
        choice.Content = new ScrollViewer { Content = panel };
        using var registration = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(choice.Close));
        try
        {
            token.ThrowIfCancellationRequested();
            await choice.ShowDialog(this);
            token.ThrowIfCancellationRequested();
            return selected;
        }
        finally { if (ReferenceEquals(dialog, choice)) { dialog = null; } panel.Children.Clear(); }
    }

    internal void RetireReview()
    {
        Confirm.IsEnabled = false;
        Review.Text = string.Empty;
        dialog?.Close();
    }

    internal void ClearPrivateText()
    {
        RetireReview();
        NameInput.Text = Source.Text = string.Empty;
        Inventory.ItemsSource = null;
        Status.Text = string.Empty;
    }
}
