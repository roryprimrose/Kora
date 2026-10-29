using Avalonia.Automation.Peers;
using Avalonia.Controls;

using Kora.Controls;

namespace Kora;

internal sealed partial class NativeTableControl
{
    private sealed class TableAutomationPeer(NativeTableControl table) : ControlAutomationPeer(table)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Table;
        protected override string? GetNameCore() => table.CanRead ? base.GetNameCore() : null;
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => table.CanRead ? base.GetChildrenCore() : [];
        protected override bool IsEnabledCore() => table.CanRead && base.IsEnabledCore();
    }

    private sealed class TableHeader(NativeTableControl table, int column) : Button
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new HeaderAutomationPeer(this, table, column);

        private sealed class HeaderAutomationPeer(TableHeader owner, NativeTableControl table, int column)
            : ButtonAutomationPeer(owner)
        {
            protected override string? GetNameCore() => table.CanRead ? base.GetNameCore() : null;
            // The button already names the exact header; its visual label is not a second announcement.
            protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => [];
            protected override bool IsEnabledCore() => table.CanRead && base.IsEnabledCore();
            protected override bool IsKeyboardFocusableCore() => table.CanRead && base.IsKeyboardFocusableCore();
            protected override void SetFocusCore() => table.FocusCell(new(null, column));
        }
    }

    private sealed class TableReader(NativeTableControl table, CellPosition? position = null) : NamedTextBlock
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new ReaderAutomationPeer(this, table, position);

        private sealed class ReaderAutomationPeer(TableReader owner, NativeTableControl table, CellPosition? position)
            : NamedTextBlockAutomationPeer(owner)
        {
            protected override bool CanRead => table.CanRead;
            protected override AutomationPeer? GetLabeledByCore() => CanRead ? base.GetLabeledByCore() : null;
            protected override bool IsEnabledCore() => CanRead && base.IsEnabledCore();
            protected override bool IsKeyboardFocusableCore() => CanRead && base.IsKeyboardFocusableCore();

            protected override void SetFocusCore()
            {
                if (position is not null) { table.FocusCell(position); }
            }
        }
    }
}
