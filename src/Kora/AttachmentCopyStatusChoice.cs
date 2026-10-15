using Kora.Core.Storage;

namespace Kora;

internal sealed record AttachmentCopyStatusChoice(AttachmentCopyStatus? Status)
{
    public override string ToString() => Status?.ToString() ?? "Any";
}
