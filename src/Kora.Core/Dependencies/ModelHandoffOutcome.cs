namespace Kora.Core.Dependencies;

/// <summary>Review outcomes are not runtime qualification or egress receipts.</summary>
public enum ModelHandoffOutcome
{
    Offered,
    Approved,
    Declined,
    Removed,
    Cancelled,
    Expired,
    Stale,
    Denied,
    Unavailable,
}
