namespace Kora.Windows.IntegrationTests;

internal static class NativeFixtureEvidenceMode
{
    internal const string EnvironmentVariable = "KORA_TEST_NATIVE_EVIDENCE_MODE";

    internal static bool IsBuffered(string? mode) => mode switch
    {
        null or "synchronous" => false,
        "buffered" => true,
        _ => throw new InvalidDataException("Unknown headless native fixture evidence mode."),
    };
}
