using System.ComponentModel;
using Kora.Core.Commands;

namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindMemoryContext(SessionsViewModel model)
    {
        var synchronizing = false;
        MemoryContextRecords.SelectionChanged += (_, _) =>
        {
            if (!synchronizing)
            { model.SelectMemoryContext(MemoryContextRecords.SelectedItems?.OfType<MemorySummary>().ToArray() ?? []); }
        };
        PropertyChangedEventHandler changed = (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(SessionsViewModel.MemoryContextSelection), StringComparison.Ordinal))
            { return; }
            synchronizing = true;
            try
            {
                MemoryContextRecords.SelectedItems?.Clear();
                foreach (var row in model.MemoryContextSelection) { MemoryContextRecords.SelectedItems?.Add(row); }
            }
            finally { synchronizing = false; }
        };
        model.PropertyChanged += changed;
        Closed += (_, _) => { model.PropertyChanged -= changed; model.CancelMemoryContext(); };
        ReviewMemoryContext.Click += async (_, _) => await model.ReviewMemoryContextAsync();
        ConfirmMemoryContext.Click += async (_, _) => await model.ConfirmMemoryContextAsync();
        CancelMemoryContext.Click += (_, _) => model.CancelMemoryContext();
    }
}
