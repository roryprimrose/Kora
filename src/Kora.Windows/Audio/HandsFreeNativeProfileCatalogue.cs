using System.Security.Cryptography;
using System.Text;
using Kora.Windows.Dependencies;

namespace Kora.Windows.Audio;

internal static class HandsFreeNativeProfileCatalogue
{
    internal static HandsFreeNativeProfile CreatePreparedProfile(ISpeechComponentFiles files)
    {
        var component = HandsFreeSetupCatalogue.Component;
        var root = Path.Combine(files.Root, component.Identity.StorageName);
        var assets = component.Assets.Select(asset =>
            new HandsFreeNativeAsset(Path.Combine(root, asset.Name), asset.SizeBytes, asset.Sha256)).ToList();
        assets.Add(new(HandsFreeSetupCatalogue.NativePath, HandsFreeSetupCatalogue.NativeBytes, HandsFreeSetupCatalogue.NativeSha256));
        assets.Add(new(Path.Combine(root, ".kora-component-v1"), 64,
            Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(component.ManifestDigest)))));
        return new(HandsFreeSetupCatalogue.NativePath, root, Path.Combine(root, "dictionary.data"),
            Path.Combine(root, "silero.onnx"), assets, [], qualification: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mdef"] = Path.Combine(root, "mdef.data"),
                ["mean"] = Path.Combine(root, "means.data"),
                ["var"] = Path.Combine(root, "variances.data"),
                ["tmat"] = Path.Combine(root, "transition.data"),
                ["sendump"] = Path.Combine(root, "sendump.data"),
                ["fdict"] = Path.Combine(root, "noise.data"),
                ["featparams"] = Path.Combine(root, "features.data"),
            });
    }
}
