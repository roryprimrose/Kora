using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

internal sealed class ProviderProfileConfigurationFixture : IAsyncDisposable
{
    internal ProviderProfileConfigurationFixture()
    {
        Admission = new(Store, Store, new HostTaskCoordinator(Store));
        Policy = new(Call);
        Service = CreateService();
    }

    internal bool Eligible { get; set; } = true;
    internal ProfilePreferences Preferences { get; } = new();
    internal ProfileCatalogue Catalogue { get; } = new();
    internal ProfileTime Time { get; } = new();
    internal ProfileAudit Audit { get; } = new();
    internal ProfileCall Call { get; } = new();
    internal AudioControlTestStore Store { get; } = new();
    internal AudioControlAdmission Admission { get; }
    internal CallCommunicationPolicy Policy { get; }
    internal ProviderProfileConfigurationService Service { get; }
    internal CancellationToken Token => TestContext.Current.CancellationToken;
    internal ProviderProfileConfigurationService CreateService() => new(Preferences, Catalogue, Admission, Audit, Time);
    internal Task Refresh(RequestOrigin origin = RequestOrigin.LocalUi) => Service.RefreshAsync(origin, () => Eligible, Token);
    internal Task<bool> Select(ProviderService service) => Apply(Service.Choices.Single(choice => choice.Service == service));
    internal Task<bool> Apply(ProviderProfileChoice choice, RequestOrigin origin = RequestOrigin.LocalUi, long? callRevision = null,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand, CancellationToken? token = null) =>
        Service.SelectAsync(choice, callRevision ?? Policy.Current.Revision, origin, initiator, Policy, () => Eligible, token ?? Token);
    internal ProviderProfileChoice TupleChoice()
    {
        var model = Service.Models.Single();
        return Service.CreateTupleChoice(model, model.Reasoning[0], model.Context[0]);
    }
    public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }

    internal sealed class ProfileTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        internal Action? AfterRead { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            var observed = Now;
            AfterRead?.Invoke();
            return observed;
        }
    }

    internal sealed class ProfilePreferences : IProviderProfilePreferences
    {
        internal ProviderProfilePreferences? Value { get; set; }
        internal bool Pending { get; set; }
        internal int Writes { get; private set; }
        internal Exception? LoadFailure { get; set; }
        internal Exception? SaveFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterConfirm { get; set; }
        internal Action? AfterRead { get; set; }
        public ProviderProfilePreferences? Load() => Pending ? throw new InvalidDataException("Unconfirmed evidence") : ReadBack();
        public ProviderProfilePreferences? ReadBack()
        {
            if (LoadFailure is { } error) { throw error; }
            var value = Value;
            AfterRead?.Invoke();
            return value;
        }
        public void BeginWrite() => Pending = true;
        public void Save(ProviderProfilePreferences preferences)
        {
            if (SaveFailure is { } error) { throw error; }
            Value = preferences;
            Writes++;
            AfterWrite?.Invoke();
        }
        public void ConfirmWrite()
        {
            if (ConfirmFailure is { } error) { throw error; }
            Pending = false;
            AfterConfirm?.Invoke();
        }
    }

    internal sealed class ProfileCatalogue : IProviderCapabilityCatalogue
    {
        internal LocalProviderInstallation Installation { get; set; } = LocalProviderInstallation.Installed;
        internal ProviderCatalogueStatus Status { get; set; } = ProviderCatalogueStatus.Unavailable;
        internal string Account { get; set; } = "account";
        internal long Revision { get; set; } = 1;
        internal string Model { get; set; } = "exact-model";
        internal string Reasoning { get; set; } = "managed-effort";
        internal string Context { get; set; } = "managed-window";
        internal int Calls { get; private set; }
        internal Func<Task>? BeforeObservation { get; set; }
        internal Action? AfterObservation { get; set; }
        internal DateTimeOffset Expiry { get; set; } = DateTimeOffset.UnixEpoch.AddMinutes(10);
        internal bool LargeCatalogue { get; set; }
        internal bool FieldCatalogue { get; set; }
        public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (BeforeObservation is { } wait) { await wait().ConfigureAwait(false); }
            cancellationToken.ThrowIfCancellationRequested();
            var observation = new ProviderCatalogueObservation(Installation,
                Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset).Select(service =>
                {
                    var selection = ModelProviderSelection.OllamaCandidate with { Revision = new(Revision), Provider = service switch
                    {
                        ProviderService.Local => ModelProviderIdentity.Ollama,
                        ProviderService.Copilot => ModelProviderIdentity.Copilot,
                        ProviderService.Claude => ModelProviderIdentity.Claude,
                        _ => ModelProviderIdentity.Codex,
                    } };
                    return new ProviderCapabilityCatalogue(service, Status, Status == ProviderCatalogueStatus.Current ? new(Account) : null,
                        new(Revision), DateTimeOffset.UnixEpoch.AddMinutes(-1), Expiry, Status != ProviderCatalogueStatus.Current ? []
                            : FieldCatalogue
                                ? [
                                    new(new(Model), "Same inert label", selection,
                                        [new(new(Reasoning), "Same inert label", false), new(new("exact-effort"), "Same inert label", false)],
                                        [new(new(Context), "Same inert label", false, 4096), new(new("exact-window"), "Same inert label", false, 8192)]),
                                    new(new("compatible-model"), "Same inert label", selection with { Model = new(Guid.Parse("11111111-1111-1111-1111-111111111111")) },
                                        [new(new(Reasoning), "Same inert label", false), new(new("exact-effort"), "Same inert label", false)],
                                        [new(new(Context), "Same inert label", false, 4096), new(new("exact-window"), "Same inert label", false, 8192)]),
                                    new(new("incompatible-model"), "Same inert label", selection with { Model = new(Guid.Parse("22222222-2222-2222-2222-222222222222")) },
                                        [new(new("other-effort"), "Same inert label", false)], [new(new("other-window"), "Same inert label", false, 1024)]),
                                ]
                            : LargeCatalogue
                                ? Enumerable.Range(0, 64).Select(index => new ProviderCatalogueModel(new(Model + index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                                    new string('M', 256), selection with { Model = new(new Guid(index + 1, 0, 0, new byte[8])) },
                                    Enumerable.Range(0, 16).Select(option => new ProviderProfileOption(new(Reasoning + option.ToString(System.Globalization.CultureInfo.InvariantCulture)), new string('R', 256), false)).ToArray(),
                                    Enumerable.Range(0, 16).Select(option => new ProviderProfileOption(new(Context + option.ToString(System.Globalization.CultureInfo.InvariantCulture)), new string('C', 256), false, 4096)).ToArray())).ToArray()
                                : [new(new(Model), "Advertised fake model", selection,
                                    [new(new(Reasoning), "Provider-managed reasoning", true)],
                                    [new(new(Context), "Provider-managed context", true)])]);
                }).ToArray());
            AfterObservation?.Invoke();
            return observation;
        }
    }

    internal sealed class ProfileAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest?> Hosts { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            BeforeWrite?.Invoke(auditEvent);
            Events.Add(auditEvent);
            Hosts.Add(HostActivity.Current?.Request);
        }
    }

    internal sealed class ProfileCall : ICallStateService
    {
        private CallState state = CallState.Clear;
        internal CallState State { get => state; set { state = value; StateChanged?.Invoke(this, new(value)); } }
        public CallState CurrentState => State;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    }
}
