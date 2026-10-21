using Kora.Application.Presentation;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class AllWorkViewModel
{
    private string selectedDetails = string.Empty;
    private AdmittedMetadataFindState? find;
    private Func<bool>? findAdmission;
    private long findVersion;
    private string findQuery = string.Empty;
    private string findStatus = string.Empty;
    private string findMatch = string.Empty;
    private bool canFind;
    private bool canFindNavigate;

    public string FindQuery => findQuery;
    public string FindStatus => findStatus;
    public string FindMatch => findMatch;
    public bool CanFind => canFind;
    public bool CanFindNavigate => canFindNavigate;
    internal long FindVersion => findVersion;

    private void AdmitFind()
    {
        if (selected is null || page is null || !CanEdit || pageControlRevision != access.ControlRevision) { return; }
        var row = selected;
        var admittedPage = page;
        var source = selectedDetails;
        var revision = access.ControlRevision;
        var admittedEpoch = epoch;
        findAdmission = () => !closed && !busy && access.CanInspect && revision == access.ControlRevision
            && admittedEpoch == epoch && ReferenceEquals(page, admittedPage)
            && ReferenceEquals(selected, row) && ReferenceEquals(selectedDetails, source);
        find = new(source, findAdmission, counted: true);
        CacheFind();
    }

    private bool FindCurrent() => findAdmission?.Invoke() == true;

    private void RetireFind()
    {
        findVersion++;
        find?.Dispose();
        find = null;
        findAdmission = null;
        findQuery = findStatus = findMatch = string.Empty;
        canFind = canFindNavigate = false;
    }

    internal void EditFind(string value)
    {
        findVersion++;
        if (!FindCurrent()) { RetireFind(); }
        else { find!.Edit(value); }
        CacheFind();
        NotifyFind();
    }

    internal void ClearFind()
    {
        findVersion++;
        if (!FindCurrent()) { RetireFind(); }
        else { find!.Clear(); }
        CacheFind();
        NotifyFind();
    }

    internal void RetireDisplayedFind()
    {
        RetireFind();
        NotifyFind();
    }

    internal long FindMetadata(SnapshotFindDirection direction)
    {
        var version = ++findVersion;
        if (!FindCurrent()) { RetireFind(); }
        else { find!.Find(direction); }
        CacheFind();
        NotifyFind();
        return version;
    }

    internal bool HasCurrentFindMatch(long version)
    {
        if (!FindCurrent())
        {
            RetireFind();
            NotifyFind();
            return false;
        }
        return version == findVersion && findMatch.Length != 0;
    }

    internal bool ValidateFind()
    {
        if (FindCurrent()) { return true; }
        RetireFind();
        NotifyFind();
        return false;
    }

    internal void ReportFindFocusUnavailable(long version)
    {
        if (!HasCurrentFindMatch(version)) { return; }
        find!.ReportFocusUnavailable();
        CacheFind();
        NotifyFind();
    }

    internal void ReportFindQueryFocusUnavailable(long version)
    {
        if (!ValidateFind() || version != findVersion) { return; }
        ClearFind();
        findStatus = "Metadata Find query focus is unavailable; explicitly select the row and retry Ctrl F.";
        NotifyFind();
    }

    private void CacheFind()
    {
        var admitted = find;
        if (admitted is null) { return; }
        var query = admitted.Query;
        var statusText = admitted.Status;
        var matchText = admitted.ExactMatch;
        var navigation = admitted.CanNavigate;
        if (!ReferenceEquals(find, admitted)) { return; }
        if (!admitted.Available || !FindCurrent()) { RetireFind(); return; }
        findQuery = query;
        findStatus = statusText;
        findMatch = matchText;
        canFind = true;
        canFindNavigate = navigation;
    }

    private void NotifyFind()
    {
        OnPropertyChanged(nameof(FindQuery));
        OnPropertyChanged(nameof(FindStatus));
        OnPropertyChanged(nameof(FindMatch));
        OnPropertyChanged(nameof(CanFind));
        OnPropertyChanged(nameof(CanFindNavigate));
    }
}
