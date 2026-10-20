namespace Kora.Core.Storage;

/// <summary>Presentation order of one fully admitted bounded metadata page, never storage order.</summary>
public enum SessionAttachmentPageOrder
{
    Source,
    DisplayNameAscending,
    DisplayNameDescending,
    RetainedBytesAscending,
    RetainedBytesDescending,
}
