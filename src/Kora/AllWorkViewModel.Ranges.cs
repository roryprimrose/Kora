using Kora.Application.Infrastructure;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AllWorkViewModel
{
    private IReadOnlyList<WorkRangeInput>? rangeInputs;
    public IReadOnlyList<WorkRangeInput> RangeInputs => rangeInputs ??=
    [
        CreateRangeInput("Authority revision"),
        CreateRangeInput("Queue revision"),
        CreateRangeInput("Pending count"),
        CreateRangeInput("Pending capacity"),
        CreateRangeInput("Execution slots"),
        CreateRangeInput("Observed current fixed reads"),
        CreateRangeInput("Omitted queue receipts"),
        CreateRangeInput("Omitted tasks"),
        CreateRangeInput("Omitted pending questions")
    ];

    public string RangeHint => TryRanges(out _, out var error)
        ? "Optional inclusive minimum/maximum; empty means Any. ASCII decimal Int64 only. Authority revision, capacity and slots "
            + "must be positive; queue revision and counts may be zero. Minimum must not exceed maximum. "
            + "Only Observed work matches ranges. Omission counts never prove absence, completeness, success or authority. Editing reads nothing."
        : "Invalid work metadata range: " + error + " No read performed; correct or clear the bounds.";

    private WorkRangeInput CreateRangeInput(string label) => new(label, () => CanEdit, Invalidate, () =>
    {
        status = RangeHint;
        Notify();
    });

    private bool TryRanges(out SessionWorkOverviewRanges ranges, out string error)
    {
        try
        {
            ranges = new(RangeInputs[0].Parse(), RangeInputs[1].Parse(), RangeInputs[2].Parse(),
                RangeInputs[3].Parse(), RangeInputs[4].Parse(), RangeInputs[5].Parse(),
                RangeInputs[6].Parse(), RangeInputs[7].Parse(), RangeInputs[8].Parse());
            ranges.Validate();
            error = string.Empty;
            return true;
        }
        catch (InvalidDataException exception)
        {
            ranges = default;
            error = exception.Message;
            return false;
        }
    }

    public void ClearRanges()
    {
        if (!CanEdit) { return; }
        Invalidate();
        foreach (var input in RangeInputs) { input.Clear(); }
        status = "Work metadata ranges cleared. No read performed; Refresh starts a new bounded scan.";
        Notify();
    }

    public sealed class WorkRangeInput(string label, Func<bool> canEdit, Action retire, Action changed) : ObservableObject
    {
        private string minimum = string.Empty;
        private string maximum = string.Empty;
        public string Label => label;
        public string MinimumLabel => label + " inclusive minimum; empty means Any; editing performs no read";
        public string MaximumLabel => label + " inclusive maximum; empty means Any; editing performs no read";
        public string Minimum
        {
            get => minimum;
            set => Edit(ref minimum, value, nameof(Minimum));
        }
        public string Maximum
        {
            get => maximum;
            set => Edit(ref maximum, value, nameof(Maximum));
        }

        private void Edit(ref string field, string value, string property)
        {
            if (!canEdit() || string.Equals(field, value, StringComparison.Ordinal)) { return; }
            retire();
            field = value;
            OnPropertyChanged(property);
            changed();
        }

        internal SessionWorkMetadataRange Parse() => SessionWorkMetadataRange.Parse(minimum, maximum);
        internal void Clear()
        {
            minimum = string.Empty;
            maximum = string.Empty;
            OnPropertyChanged(nameof(Minimum));
            OnPropertyChanged(nameof(Maximum));
        }
    }
}
