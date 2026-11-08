namespace Kora.Core.Presentation;

public interface IMarkdownDisplayPreferences
{
    MarkdownDisplayMode? Load();
    MarkdownDisplayMode? ReadBack();
    void BeginWrite();
    void Save(MarkdownDisplayMode mode);
    void ConfirmWrite();
}
