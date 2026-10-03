namespace Kora.Core.Communication;

public interface ICallAwarePreferences
{
    CallAwareSettings? Load();

    void Save(CallAwareSettings settings);
}
