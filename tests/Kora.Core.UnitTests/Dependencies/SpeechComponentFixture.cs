using System.Security.Cryptography;
using Kora.Core.Dependencies;

namespace Kora.Testing;

internal static class SpeechComponentFixture
{
    internal static byte[] Bytes => [1, 2, 3, 4];
    internal static SpeechComponentDefinition Definition(string? qualification = "fixture-qualified",
        SpeechComponentIdentity? identity = null, IEnumerable<SpeechComponentAsset>? assets = null,
        long staging = 16, long installed = 8, long ram = 32, long vram = 0) =>
        new(identity ?? new("fixture-input", "v1"), "Fixture only, not a production offer", "fixture-runtime-v1",
            "fixture-model-v1", "Fixture licence, no production redistribution decision",
            new Uri("https://speech.example.invalid/licence"), qualification, staging, installed, ram, vram,
            assets ?? [Asset("model.data"), Asset("tokens.data")]);

    internal static SpeechComponentAsset Asset(string name) => new(name,
        new Uri("https://speech.example.invalid/" + name), Bytes.Length,
        Convert.ToHexString(SHA256.HashData(Bytes)));
}
