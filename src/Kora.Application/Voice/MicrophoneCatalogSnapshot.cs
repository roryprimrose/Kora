using Kora.Core.Voice;
using Kora.Core.Platform;

namespace Kora.Application.Voice;

public sealed record MicrophoneCatalogSnapshot(
    IReadOnlyList<MicrophoneDevice> Devices,
    MicrophoneDevice? DefaultMicrophone,
    WindowsPrivacySnapshot Privacy,
    MicrophoneAccessStatus Access);
