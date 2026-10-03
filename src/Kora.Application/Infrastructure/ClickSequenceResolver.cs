namespace Kora.Application.Infrastructure;

internal sealed class ClickSequenceResolver
{
    private bool hasPendingClick;

    public ClickSequenceOutcome RegisterClick()
    {
        if (hasPendingClick)
        {
            hasPendingClick = false;
            return ClickSequenceOutcome.DoubleClick;
        }

        hasPendingClick = true;
        return ClickSequenceOutcome.Pending;
    }

    public ClickSequenceOutcome ResolvePendingClick()
    {
        if (!hasPendingClick)
        {
            return ClickSequenceOutcome.None;
        }

        hasPendingClick = false;
        return ClickSequenceOutcome.SingleClick;
    }

    public void Cancel() => hasPendingClick = false;
}
