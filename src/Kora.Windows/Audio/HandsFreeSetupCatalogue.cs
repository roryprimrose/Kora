using Kora.Core.Dependencies;

namespace Kora.Windows.Audio;

/// <summary>Preparation admission only. This catalogue grants no acoustic or capture qualification.</summary>
public static class HandsFreeSetupCatalogue
{
    public const string PocketSphinxCommit = "511126b492dcb267cf30d49d631946d7b61a9530";
    public const string SileroCommit = "be95df9152c0d7618fa1edfeb296fc3dae32376f";
    public const string DictionaryNoticeCommit = "74790861f652b15e4ac49015a90074ad62a27690";
    public const string NativeFileName = "pocketsphinx-win-x64.dll";
    public const long NativeBytes = 668672;
    public const string NativeSha256 = "725609787580AA759A9D9A62B1A7E6136408346D159D9E4681AFA1AF9FFAE69B";

    public static SpeechComponentDefinition Component { get; } = Create();
    public static SpeechComponentCatalogue Production { get; } = new([Component]);
    public static string NativePath => Path.Combine(AppContext.BaseDirectory, "HandsFreeNative", NativeFileName);

    private static SpeechComponentDefinition Create()
    {
        SpeechComponentAsset Pocket(string name, string path, long bytes, string hash) =>
            new(name, new($"https://raw.githubusercontent.com/cmusphinx/pocketsphinx/{PocketSphinxCommit}/{path}"), bytes, hash);
        SpeechComponentAsset Silero(string name, string path, long bytes, string hash) =>
            new(name, new($"https://raw.githubusercontent.com/snakers4/silero-vad/{SileroCommit}/{path}"), bytes, hash);
        SpeechComponentAsset[] assets =
        [
            Pocket("dictionary.data", "model/en-us/cmudict-en-us.dict", 3275645, "20B5C293E1F311FB375FE067E500EC5636F4FC7AF5594967263696DEF9B23BFE"),
            Pocket("acoustic-licence.txt", "model/en-us/en-us/README", 1617, "8B88DE980568509C646D0527B8414BEEF136964391903B40996D32F737BF752E"),
            Pocket("features.data", "model/en-us/en-us/feat.params", 164, "C982C5F75E2A30C34D2C9EF1F6B129A5A00E67E3E927565076B58664CC2404C9"),
            Pocket("mdef.data", "model/en-us/en-us/mdef", 2959176, "2360F9A86889C1CFEE8BD618A0269387911E5FB2920A594F506B18B8C79683B0"),
            Pocket("means.data", "model/en-us/en-us/means", 838732, "832019E32CAC12EB318964F96F469034ACB12D0348EEDDC3831831A100CB4DD4"),
            Pocket("noise.data", "model/en-us/en-us/noisedict", 56, "7295B07DF2C204C4F87C6782B6BE1A3859D7006D4E3864181C955D6DAB105A33"),
            Pocket("sendump.data", "model/en-us/en-us/sendump", 1969024, "8C9564C0D5BEF69CA9D9BF1014ABE162F071644CF02CF1FA8A483C3DC165A7A8"),
            Pocket("transition.data", "model/en-us/en-us/transition_matrices", 2080, "C1F7F28EA43177BE734BE1F88BD7F1B9A853D0E660F8599C67C6EAECA8BB539A"),
            Pocket("variances.data", "model/en-us/en-us/variances", 838732, "B00D696F85E96834FC10F8E5F06428D8C4DB6BFFDBE5845B6F69BF6EFBC48FA5"),
            Pocket("pocketsphinx-licence.txt", "LICENSE", 6697, "1C3B5C5529D7360C927AA034EEA28010C59558C1F67FB722D8D8B5D69CB98A23"),
            new("dictionary-licence.txt", new($"https://raw.githubusercontent.com/cmusphinx/cmudict/{DictionaryNoticeCommit}/LICENSE"),
                1754, "BD4CE8E44170A5F9F481310CA85C51DE3C4F851A65E679B40E603B143BD3542A"),
            Silero("silero.onnx", "src/silero_vad/data/silero_vad.onnx", 2327524, "1A153A22F4509E292A94E67D6F9B85E8DEB25B4988682B7E174C65279D8788E3"),
            Silero("silero-licence.txt", "LICENSE", 1075, "2E63E9A38B6E8FC0C7BC37CE174CACA1862870856C6DAF5697CFB785E925520B"),
        ];
        var bytes = assets.Sum(asset => asset.SizeBytes);
        return new(new("hands-free-cpu-data", "v1"), "Hands-free CPU assets (capture remains unavailable)",
            "pocketsphinx-5-1-1-silero-6-2",
            "PocketSphinx 5.1.1 en-US / CMU dictionary / Silero 6.2; Windows x64 CPU; 512 MiB RAM planning budget, not a measured runtime claim",
            "CMU and Alpha Cephei BSD-style; Silero MIT; notices included",
            new($"https://github.com/cmusphinx/pocketsphinx/blob/{PocketSphinxCommit}/LICENSE"),
            "asset-preparation-v1", checked(bytes * 2 + 64), bytes + 64, 536870912, 0, assets);
    }
}
