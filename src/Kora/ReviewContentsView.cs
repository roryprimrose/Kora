using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Core.Presentation;

namespace Kora;

/// <summary>
/// Passive, read-only presentation of the exact files an item under review will use. It owns no
/// approval, enablement or execution action; the originating review keeps its own confirmation.
/// </summary>
internal sealed class ReviewContentsView : UserControl
{
    private readonly ReviewContents contents;
    private readonly Dictionary<TreeViewItem, ReviewDocument> documents = [];

    internal ReviewContentsView(ReviewContents contents)
    {
        this.contents = contents;
        Name = "ReviewContents";
        AutomationProperties.SetName(this, "Contents of " + contents.Title);

        Tree = new TreeView { Name = "ReviewContentsTree", MinWidth = 140 };
        AutomationProperties.SetName(Tree, "Files in " + contents.Title);
        DocumentPath = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        DocumentDigest = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
        DocumentDigest.Classes.Add("muted");
        DocumentText = new TextBox
        {
            Name = "ReviewContentsText",
            IsReadOnly = true,
            IsUndoEnabled = false,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12,
            ContextFlyout = null,
            ContextMenu = null,
        };
        AutomationProperties.SetName(DocumentText, "Exact read-only file contents");

        BuildTree();
        Tree.SelectionChanged += (_, _) => ShowSelected();

        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(new TextBlock { Text = contents.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var summary = new TextBlock
        {
            Text = $"{contents.Documents.Length} file{(contents.Documents.Length == 1 ? string.Empty : "s")} · revision {Short(contents.Revision)} · read-only, viewing is not approval",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        summary.Classes.Add("muted");
        heading.Children.Add(summary);
        if (!contents.Unavailable.IsEmpty)
        {
            UnavailableNotice = new TextBlock
            {
                Name = "ReviewContentsUnavailable",
                Text = "Not included: " + string.Join("; ", contents.Unavailable),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            };
            heading.Children.Add(UnavailableNotice);
        }

        var viewer = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 4 };
        viewer.Children.Add(DocumentPath);
        Grid.SetRow(DocumentDigest, 1);
        viewer.Children.Add(DocumentDigest);
        Grid.SetRow(DocumentText, 2);
        viewer.Children.Add(DocumentText);

        var body = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 };
        body.Children.Add(heading);
        var panes = new Grid();
        if (IsSingleDocument)
        {
            panes.Children.Add(viewer);
        }
        else
        {
            panes.ColumnDefinitions = new("Auto,6,*");
            panes.Children.Add(Tree);
            var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Focusable = true };
            AutomationProperties.SetName(splitter, "Resize file list");
            Grid.SetColumn(splitter, 1);
            panes.Children.Add(splitter);
            Grid.SetColumn(viewer, 2);
            panes.Children.Add(viewer);
        }
        Grid.SetRow(panes, 1);
        body.Children.Add(panes);
        Content = new Border { Padding = new(12), Child = body };

        Select(contents.Documents[0]);
    }

    internal ReviewContents Contents => contents;
    internal TextBlock? UnavailableNotice { get; }
    internal TreeView Tree { get; }
    internal TextBlock DocumentPath { get; }
    internal TextBlock DocumentDigest { get; }
    internal TextBox DocumentText { get; }
    internal bool IsSingleDocument => contents.Documents.Length == 1;
    internal ReviewDocument? Selected { get; private set; }

    internal void Select(ReviewDocument document)
    {
        var item = documents.First(pair => ReferenceEquals(pair.Value, document)).Key;
        if (!ReferenceEquals(Tree.SelectedItem, item)) { Tree.SelectedItem = item; }
        Display(document);
    }

    private void BuildTree()
    {
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.Ordinal);
        foreach (var document in contents.Documents.OrderBy(TreeOrder, StringComparer.OrdinalIgnoreCase))
        {
            var segments = document.Segments;
            ItemCollection siblings = Tree.Items;
            var prefix = string.Empty;
            for (var index = 0; index < segments.Length - 1; index++)
            {
                prefix = prefix.Length == 0 ? segments[index] : prefix + "/" + segments[index];
                if (!folders.TryGetValue(prefix, out var folder))
                {
                    folder = new TreeViewItem { Header = segments[index] + "/", IsExpanded = true };
                    AutomationProperties.SetName(folder, "Folder " + prefix);
                    siblings.Add(folder);
                    folders.Add(prefix, folder);
                }
                siblings = folder.Items;
            }
            var file = new TreeViewItem { Header = segments[^1] };
            AutomationProperties.SetName(file, "File " + document.Path);
            documents.Add(file, document);
            siblings.Add(file);
        }
    }

    private void ShowSelected()
    {
        if (Tree.SelectedItem is TreeViewItem item && documents.TryGetValue(item, out var document)) { Display(document); }
    }

    private void Display(ReviewDocument document)
    {
        Selected = document;
        DocumentPath.Text = document.Path;
        DocumentDigest.Text = "SHA-256 " + document.Digest;
        DocumentText.Text = document.Text;
        DocumentText.CaretIndex = 0;
    }

    // Folders sort before files at each level, matching familiar file explorers.
    private static string TreeOrder(ReviewDocument document) =>
        string.Join('/', document.Segments.Select((segment, index) => (index < document.Segments.Length - 1 ? "0" : "1") + segment));

    private static string Short(string revision) => revision.Length > 12 ? revision[..12] : revision;
}
