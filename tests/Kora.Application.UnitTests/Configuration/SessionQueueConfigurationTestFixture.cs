using System.Diagnostics;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

internal sealed class SessionQueueConfigurationTestFixture : IAsyncDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    internal SessionQueueConfigurationTestFixture()
    {
        ActivitySource.AddActivityListener(listener);
        Admission = new(Store, Store, new HostTaskCoordinator(Store));
        Policy = new(Call);
        Service = Create();
    }
    internal bool Eligible { get; set; } = true;
    internal CancellationToken Token => TestContext.Current.CancellationToken;
    internal AudioControlTestStore Store { get; } = new();
    internal Preferences PreferencesStore { get; } = new();
    internal Audit AuditLog { get; } = new();
    internal CallSource Call { get; } = new();
    internal CallCommunicationPolicy Policy { get; }
    internal AudioControlAdmission Admission { get; }
    internal SessionQueueConfigurationService Service { get; }
    internal SessionQueueConfigurationService Create() => new(PreferencesStore, Admission, AuditLog);
    internal Task Refresh(RequestOrigin origin = RequestOrigin.LocalUi) => Service.RefreshAsync(origin, () => Eligible, Token);
    internal SessionQueueConfigurationProposal Propose(string? value, SessionQueueOption option = SessionQueueOption.PendingPerSession) =>
        Service.Propose(option, value, Service.Get().Revision, Policy.Current.Revision);
    internal Task<bool> Apply(SessionQueueConfigurationProposal proposal, RequestOrigin origin = RequestOrigin.LocalUi,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
        Service.ApplyAsync(proposal, origin, initiator, Policy, () => Eligible, Token);
    internal Task<bool> Set(string? value, SessionQueueOption option = SessionQueueOption.PendingPerSession) => Apply(Propose(value, option));
    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        await Admission.DisposeAsync();
        Policy.Dispose();
        listener.Dispose();
    }

    internal sealed class Preferences : ISessionQueuePreferences
    {
        internal SessionQueuePreferences Value { get; set; } = new();
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal Exception? BeginFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterConfirm { get; set; }
        internal int Writes { get; private set; }
        internal bool Pending { get; set; }
        public SessionQueuePreferences Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed write"); }
            return ReadBack();
        }
        public SessionQueuePreferences ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() { if (BeginFailure is { } failure) { throw failure; } Pending = true; }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
        public void Save(SessionQueuePreferences value)
        {
            if (WriteFailure is { } failure) { throw failure; }
            Value = value;
            Writes++;
            AfterWrite?.Invoke();
        }
    }
    internal sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent item)
        {
            BeforeWrite?.Invoke(item);
            HostActivity.RequireCurrent();
            Events.Add(item);
        }
    }
    internal sealed class CallSource : ICallStateService
    {
        public CallState CurrentState { get; private set; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        internal void Set(CallState value) { CurrentState = value; StateChanged?.Invoke(this, new(value)); }
    }
}
