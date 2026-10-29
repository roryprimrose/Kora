using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration.RegisteredSettings;

/// <summary>Host-created evidence of one exact successful preference write, never persisted.</summary>
internal sealed record RegisteredSettingChange(
    RegisteredSettingDescriptor Descriptor, string Before, string After, Func<bool> Matches,
    Func<RequestOrigin, SecurityAuditInitiator, CallCommunicationPolicy, long, Func<bool>, CancellationToken, Task<bool>> Restore);
