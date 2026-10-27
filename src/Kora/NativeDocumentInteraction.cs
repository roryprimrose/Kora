using System.Collections.Immutable;
using System.Text;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kora;

internal sealed class NativeDocumentInteraction(Func<bool> canRead, Action<string> reportStatus)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Dictionary<string, TextBlock> headings = new(StringComparer.Ordinal);
    private readonly List<(Button Button, EventHandler<RoutedEventArgs> Handler)> links = [];
    private readonly List<NativeTableControl> tables = [];
    private Func<bool>? admission = canRead;
    private Action<string>? status = reportStatus;

    internal bool CanInteract => admission?.Invoke() == true;

    internal void RegisterHeading(string id, TextBlock control)
    {
        headings.Add(id, control);
        control.Focusable = true;
        AutomationProperties.SetName(control, $"Document heading: {control.Text}");
    }

    internal IEnumerable<Button> CreateLinks(ImmutableArray<NativeDocumentLink> localLinks)
    {
        foreach (var link in localLinks)
        {
            var button = new Button { Content = $"Go to heading: {link.Label}" };
            AutomationProperties.SetName(button, $"Document-local heading link: {link.Label}");
            EventHandler<RoutedEventArgs> handler = (_, _) => Activate(link.Destination);
            button.Click += handler;
            links.Add((button, handler));
            yield return button;
        }
    }

    internal NativeTableControl CreateTable(NativeTableData data)
    {
        var table = new NativeTableControl(data, this);
        tables.Add(table);
        return table;
    }

    internal void Report(string message) => status?.Invoke(message);

    internal void Activate(string destination)
    {
        if (!CanInteract)
        {
            Report("Document navigation unavailable: this exact document is closed or no longer accessible.");
            return;
        }
        var id = DecodeFragment(destination);
        if (id is null)
        {
            Report("Document navigation rejected: malformed or non-local fragment. No external navigation occurred.");
            return;
        }
        if (!headings.TryGetValue(id, out var target))
        {
            Report("Document heading not found in this exact result; reading position unchanged.");
            return;
        }
        if (TopLevel.GetTopLevel(target) is null || !target.IsEffectivelyVisible)
        {
            Report("Document heading unavailable in the current rendered view; no focus claimed.");
            return;
        }
        target.BringIntoView();
        if (!CanInteract) { Report("Document navigation retired before focus."); return; }
        var focused = target.Focus();
        if (!CanInteract) { Report("Document navigation retired during focus."); return; }
        Report(focused ? "Focused the exact document heading."
            : "Document heading focus unavailable; no focus claimed.");
    }

    private static string? DecodeFragment(string destination)
    {
        if (destination.Length < 2 || destination[0] != '#') { return null; }
        var decoded = new StringBuilder();
        try
        {
            for (var index = 1; index < destination.Length;)
            {
                if (destination[index] != '%') { decoded.Append(destination[index++]); continue; }
                var bytes = new List<byte>();
                while (index < destination.Length && destination[index] == '%')
                {
                    if (index + 2 >= destination.Length
                        || !byte.TryParse(destination.AsSpan(index + 1, 2),
                            System.Globalization.NumberStyles.AllowHexSpecifier,
                            System.Globalization.CultureInfo.InvariantCulture, out var value))
                    { return null; }
                    bytes.Add(value);
                    index += 3;
                }
                decoded.Append(Utf8.GetString(bytes.ToArray()));
            }
            var id = decoded.ToString();
            _ = Utf8.GetByteCount(id);
            return id.Length == 0 || id.Any(char.IsControl) ? null : id;
        }
        catch (DecoderFallbackException) { return null; }
        catch (EncoderFallbackException) { return null; }
    }

    internal void Retire()
    {
        admission = null;
        status = null;
        foreach (var (button, handler) in links)
        {
            button.Click -= handler;
            button.Content = null;
            AutomationProperties.SetName(button, string.Empty);
            button.IsEnabled = false;
        }
        links.Clear();
        foreach (var heading in headings.Values)
        {
            heading.Text = string.Empty;
            AutomationProperties.SetName(heading, string.Empty);
        }
        headings.Clear();
        foreach (var table in tables) { table.Retire(); }
        tables.Clear();
    }
}
