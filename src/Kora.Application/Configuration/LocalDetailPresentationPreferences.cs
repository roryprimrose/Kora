using Kora.Core.Dependencies;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

public sealed class LocalDetailPresentationPreferences : IDetailPresentationPreferences
{
    private const string FileName = "detail-presentation.txt";
    private const string PendingFileName = "detail-presentation-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalDetailPresentationPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalDetailPresentationPreferences(IPreferenceStore store) => this.store = store;

    public DetailPresentationMode? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("Detail presentation is unconfirmed. Inspect saved state and audit before explicit repair."); }
        return ReadBack();
    }

    public DetailPresentationMode? ReadBack() => DetailPresentationPreference.Parse(store.ReadText(FileName));
    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(DetailPresentationMode mode) => store.WriteText(FileName, DetailPresentationPreference.Format(mode));
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
