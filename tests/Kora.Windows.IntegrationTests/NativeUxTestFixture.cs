using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

internal static class NativeUxTestFixture
{
    internal static NativeUxFixtureSession Create(string scratchParent, bool silentCaption = false, bool exactGrants = false) =>
        new(scratchParent, silentCaption: silentCaption, exactGrants: exactGrants,
            bufferedEvidence: NativeFixtureEvidenceMode.IsBuffered(
                Environment.GetEnvironmentVariable(NativeFixtureEvidenceMode.EnvironmentVariable)));
}
