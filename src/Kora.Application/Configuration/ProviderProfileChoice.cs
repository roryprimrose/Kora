using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.Configuration;

public sealed class ProviderProfileChoice
{
    internal ProviderProfileChoice(ProviderProfilePreferences value, long revision, Guid owner,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> remainsAdmitted, ProviderProfileTuple? tuple = null,
        LocalProviderInstallation? resetInstallation = null, ProviderProfileField? field = null, ProviderProfileTuple? baseTuple = null)
    {
        Value = value;
        Revision = revision;
        Owner = owner;
        Session = session;
        Origin = origin;
        RemainsAdmitted = remainsAdmitted;
        Tuple = tuple;
        ResetInstallation = resetInstallation;
        Field = field;
        BaseTuple = baseTuple;
        Confirmation = field is null ? Guid.Empty : Guid.NewGuid();
    }

    public ProviderService Service => Value.Service;
    public string Label => Presentation.SettingsText.Service(Service);
    internal ProviderProfilePreferences Value { get; }
    internal long Revision { get; }
    internal Guid Owner { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> RemainsAdmitted { get; }
    internal ProviderProfileTuple? Tuple { get; }
    internal LocalProviderInstallation? ResetInstallation { get; }
    internal ProviderProfileField? Field { get; }
    internal ProviderProfileTuple? BaseTuple { get; }
    internal Guid Confirmation { get; }
}
