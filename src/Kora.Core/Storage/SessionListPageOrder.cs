namespace Kora.Core.Storage;

/// <summary>Presentation order within one admitted metadata page, never scan or action authority.</summary>
public enum SessionListPageOrder
{
    Source,
    NameAscending,
    NameDescending,
    GenerationAscending,
    GenerationDescending,
}
