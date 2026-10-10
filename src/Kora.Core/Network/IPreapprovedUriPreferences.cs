namespace Kora.Core.Network;

public interface IPreapprovedUriPreferences
{
    PreapprovedUriSettings Load();

    void Save(PreapprovedUriSettings settings);
}
