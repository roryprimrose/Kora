using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Core.Tools;

public sealed record RegisteredSettingMetadata(RegisteredSettingDescriptor Descriptor, bool ConfirmedValueAvailable);
