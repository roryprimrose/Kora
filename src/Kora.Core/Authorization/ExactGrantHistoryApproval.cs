namespace Kora.Core.Authorization;

/// <summary>Presence or exact identity of committed approval metadata, never approval authority.</summary>
public enum ExactGrantHistoryApproval
{
    Any,
    Present,
    Absent,
    Exact,
}
