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

internal sealed class OrdinaryDeadlineConfigurationFixture : IAsyncDisposable
{
    internal OrdinaryDeadlineConfigurationFixture()
    {
        Preferences = new(Store);
        Admission = new(Control, Control, new HostTaskCoordinator(Control));
        Service = new(Preferences, Admission, Audit);
    }
    internal DeadlinePreferenceStore Store { get; } = new();
    internal LocalOrdinaryTurnDeadlinePreferences Preferences { get; }
    internal AudioControlTestStore Control { get; } = new();
    internal AudioControlAdmission Admission { get; }
    internal DeadlineAudit Audit { get; } = new();
    internal CallCommunicationPolicy CallPolicy { get; } = new(new ClearCall());
    internal OrdinaryTurnDeadlineConfigurationService Service { get; }
    internal bool Eligible { get; set; } = true;
    internal RequestOrigin Origin { get; set; } = RequestOrigin.LocalUi;
    internal SecurityAuditInitiator Initiator { get; set; } = SecurityAuditInitiator.LocalUser;
    internal Task<OrdinaryTurnDeadlineObservation> Refresh() =>
        Service.RefreshAsync(Origin, () => Eligible, TestContext.Current.CancellationToken);
    internal Task<bool> Save(OrdinaryTurnDeadlineObservation expected, OrdinaryTurnDeadline? value) =>
        Service.SaveAsync(expected, value, Origin, Initiator, CallPolicy, CallPolicy.Current.Revision,
            () => Eligible, TestContext.Current.CancellationToken);
    public async ValueTask DisposeAsync() { CallPolicy.Dispose(); await Admission.DisposeAsync(); }

    internal sealed class DeadlinePreferenceStore : IPreferenceStore
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        internal Action<string, string>? Before { get; set; }
        internal Action<string, string>? After { get; set; }
        public string? ReadText(string fileName)
        {
            Before?.Invoke("read", fileName);
            return Values.GetValueOrDefault(fileName);
        }
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteText(string fileName, string contents)
        {
            Before?.Invoke("write", fileName);
            Values[fileName] = contents;
            After?.Invoke("write", fileName);
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName)
        {
            Before?.Invoke("delete", fileName);
            Values.Remove(fileName);
            After?.Invoke("delete", fileName);
        }
    }
    internal sealed class DeadlineAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal Action<SecurityAuditEvent>? Before { get; set; }
        public void Write(SecurityAuditEvent value)
        {
            HostActivity.RequireCurrent();
            Before?.Invoke(value);
            Events.Add(value);
        }
    }
    private sealed class ClearCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
