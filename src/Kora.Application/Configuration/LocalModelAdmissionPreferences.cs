using Kora.Core.Providers;

namespace Kora.Application.Configuration;

internal sealed class LocalModelAdmissionPreferences(IPreferenceStore store)
{
    private const string FileName = "local-model-admission.json";
    private const string PendingFileName = "local-model-admission-unconfirmed.txt";

    internal LocalModelAdmissionPreference? Read()
    {
        if (store.ReadText(PendingFileName, 64) is not null)
        { throw new InvalidDataException("Local model admission is unconfirmed. Revoke the saved admission, inspect audit evidence, and review again."); }
        return ReadBack();
    }

    internal LocalModelAdmissionPreference? ReadBack() => store.ReadText(FileName, LocalModelAdmissionPreference.MaximumBytes) is { } text
        ? LocalModelAdmissionPreference.Parse(text) : null;

    internal void BeginWrite() => store.WriteText(PendingFileName, "1");
    internal void Save(LocalModelAdmissionPreference value) => store.WriteText(FileName, value.Serialize());
    internal void Confirm() => store.Delete(PendingFileName);
    internal void Remove() => store.Delete(FileName);
}