namespace Kora.Core.Storage;

/// <summary>Display order of one admitted loaded page, never storage scan or continuation order.</summary>
public enum SavedDraftPageOrder
{
    Source,
    SessionNameAscending,
    SessionNameDescending,
    SavedRevisionAscending,
    SavedRevisionDescending,
}
