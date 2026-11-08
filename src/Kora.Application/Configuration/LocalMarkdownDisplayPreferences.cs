using Kora.Core.Dependencies;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

public sealed class LocalMarkdownDisplayPreferences : IMarkdownDisplayPreferences
{
    private const string FileName = "markdown-display.txt";
    private const string PendingFileName = "markdown-display-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalMarkdownDisplayPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalMarkdownDisplayPreferences(IPreferenceStore store) => this.store = store;

    public MarkdownDisplayMode? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("Markdown display is unconfirmed. Inspect saved state and audit before explicit repair."); }
        return ReadBack();
    }

    public MarkdownDisplayMode? ReadBack() => MarkdownDisplayPreference.Parse(store.ReadText(FileName));
    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(MarkdownDisplayMode mode) => store.WriteText(FileName, MarkdownDisplayPreference.Format(mode));
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
