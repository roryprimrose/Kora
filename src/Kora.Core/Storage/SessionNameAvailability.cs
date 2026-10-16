namespace Kora.Core.Storage;

/// <summary>Observed name metadata only, never ownership, deletion history or action authority.</summary>
public enum SessionNameAvailability
{
    Any,
    Named,
    Unnamed
}
