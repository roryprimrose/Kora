using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Kora.Controls;

/// <summary>
/// Static text that exposes its descriptive label and displayed content together.
/// Text blocks have no separate text/value automation pattern, so a label-only name
/// hides the displayed content. Unnamed instances retain the stock text-block name.
/// </summary>
public class NamedTextBlock : TextBlock
{
    protected override AutomationPeer OnCreateAutomationPeer() => new NamedTextBlockAutomationPeer(this);

    private sealed class NamedTextBlockAutomationPeer : ControlAutomationPeer
    {
        private readonly NamedTextBlock textBlock;

        internal NamedTextBlockAutomationPeer(NamedTextBlock owner) : base(owner)
        {
            textBlock = owner;
            // The stock text peer publishes raw Text changes, not the combined accessible name.
            owner.PropertyChanged += OnTextPropertyChanged;
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override bool IsControlElementCore() =>
            textBlock.TemplatedParent is null && base.IsControlElementCore();

        protected override string? GetNameCore() =>
            FormatName(AutomationProperties.GetName(textBlock), DisplayedText);

        private string? DisplayedText => textBlock.Inlines?.Text ?? textBlock.Text;

        private void OnTextPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            string? oldName;
            string? newName;
            if (args.Property == TextBlock.TextProperty)
            {
                var label = AutomationProperties.GetName(textBlock);
                oldName = FormatName(label, args.GetOldValue<string?>());
                newName = FormatName(label, args.GetNewValue<string?>());
            }
            else if (args.Property == AutomationProperties.NameProperty)
            {
                oldName = FormatName(args.GetOldValue<string?>(), DisplayedText);
                newName = FormatName(args.GetNewValue<string?>(), DisplayedText);
            }
            else
            {
                return;
            }

            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, oldName, newName);
            }
        }

        private static string? FormatName(string? label, string? text)
        {
            if (string.IsNullOrWhiteSpace(label)) { return text; }
            if (string.IsNullOrWhiteSpace(text) || string.Equals(label, text, StringComparison.Ordinal)) { return label; }
            return $"{label}: {text}";
        }
    }
}
