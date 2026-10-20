using Kora.Core.Storage;

namespace Kora;

internal sealed record AttachmentPageOrderChoice(SessionAttachmentPageOrder Value, string Label)
{
    public override string ToString() => Label;
}
