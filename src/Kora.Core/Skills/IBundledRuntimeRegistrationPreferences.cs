namespace Kora.Core.Skills;

public interface IBundledRuntimeRegistrationPreferences
{
    BundledRuntimeRegistration? Load();
    void Save(BundledRuntimeRegistration? registration);
    BundledRuntimeRegistration? LoadPending();
    void SavePending(BundledRuntimeRegistration? registration);
}
