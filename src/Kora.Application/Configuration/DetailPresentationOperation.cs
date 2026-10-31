namespace Kora.Application.Configuration;

/// <summary>Closed original-user detail preference operations, not detail navigation or offer answers.</summary>
public enum DetailPresentationOperation
{
    /// <summary>Inspect confirmed saved/default state.</summary>
    Inspect,
    /// <summary>Review a value without writing it.</summary>
    Review,
    /// <summary>Review restoring the Offer default.</summary>
    Reset,
    /// <summary>Confirm one exact pending review.</summary>
    Confirm,
    /// <summary>Cancel only this preference review.</summary>
    Cancel,
    /// <summary>Reserved but unsupported input.</summary>
    Invalid,
}
