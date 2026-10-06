using Avalonia.Input;

namespace Kora;

internal static class DetailKeyboardNavigation
{
    internal enum Action { None, CopySelection, BlockCut, BlockPaste, SelectContent, FocusSearch, ToggleSource, PreviousMatch, NextMatch, Close }

    internal static Action Resolve(Key key, KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Control) != KeyModifiers.None)
        {
            switch (key)
            {
                case Key.C:
                case Key.Insert: return Action.CopySelection;
                case Key.X: return Action.BlockCut;
                case Key.V: return Action.BlockPaste;
                case Key.A: return Action.SelectContent;
                case Key.F: return Action.FocusSearch;
                case Key.U: return Action.ToggleSource;
            }
        }
        var shift = (modifiers & KeyModifiers.Shift) != KeyModifiers.None;
        return key switch
        {
            Key.Insert when shift => Action.BlockPaste,
            Key.Delete when shift => Action.BlockCut,
            Key.F3 => shift ? Action.PreviousMatch : Action.NextMatch,
            Key.Escape => Action.Close,
            _ => Action.None,
        };
    }
}
