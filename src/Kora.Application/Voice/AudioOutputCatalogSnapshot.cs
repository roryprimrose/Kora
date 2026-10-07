using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed record AudioOutputCatalogSnapshot(
    IReadOnlyList<AudioOutputDevice> Devices, AudioOutputDevice? Default);
