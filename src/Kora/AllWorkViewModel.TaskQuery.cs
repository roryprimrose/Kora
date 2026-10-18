using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AllWorkViewModel
{
    private string taskIdText = string.Empty;
    private string requestIdText = string.Empty;

    public string TaskIdText
    {
        get => taskIdText;
        set => EditTaskQuery(ref taskIdText, value);
    }
    public string RequestIdText
    {
        get => requestIdText;
        set => EditTaskQuery(ref requestIdText, value);
    }
    public string TaskQueryHint => TryTaskQuery(out _, out var error)
        ? "Empty means Any. Exact nonempty lowercase D GUIDs only; no whitespace or repair. IDs and constrained state must "
            + "match the same validated retained task. Missing, unavailable or omitted tasks prove no absence or exhaustive search. "
            + "Correlation grants no current task/session selection, ownership, permission, approval or execution authority. Editing reads nothing."
        : "Invalid retained task correlation: " + error + " No read performed; correct or clear the IDs.";

    private void EditTaskQuery(ref string field, string value)
    {
        if (!CanEdit || string.Equals(field, value, StringComparison.Ordinal)) { return; }
        Invalidate();
        field = value;
        status = TaskQueryHint;
        Notify();
    }

    private bool TryTaskQuery(out SessionWorkOverviewTaskQuery query, out string error)
    {
        try
        {
            query = SessionWorkOverviewTaskQuery.Parse(taskIdText, requestIdText);
            error = string.Empty;
            return true;
        }
        catch (InvalidDataException exception)
        {
            query = default;
            error = exception.Message;
            return false;
        }
    }

    public void ClearTaskQuery()
    {
        if (!CanEdit) { return; }
        Invalidate();
        taskIdText = string.Empty;
        requestIdText = string.Empty;
        status = "Retained task correlation cleared. No read performed; Refresh starts a new bounded scan.";
        Notify();
    }
}
