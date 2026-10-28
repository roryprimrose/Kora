using AwesomeAssertions;
using Kora.Core.Voice;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class HandsFreeNativeReadinessTests
{
    [Fact]
    public async Task Production_without_admitted_payloads_cannot_open_even_with_a_permissive_fake_host()
    {
        var stage = new WindowsHandsFreeAudioStage();
        stage.IsAvailable.Should().BeFalse();
        var open = () => stage.OpenAsync(Binding(), TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("unknown-name")]
    [InlineData("missing-live-admission")]
    [InlineData("cancelled")]
    [InlineData("unknown-profile")]
    public async Task Exact_profile_metadata_never_substitutes_for_name_host_or_cancellation_authority(string denial)
    {
        var stage = new WindowsHandsFreeAudioStage([Profile()]);
        var binding = Binding() with
        {
            AssistantName = denial is "unknown-name" ? "Kora" : "Nova",
            CanCapture = () => denial is not "missing-live-admission",
            Profile = new(1, denial is "unknown-profile"
                ? SpeechInputSelection.Default with { ProfileId = "not-admitted" } : SpeechInputSelection.Default),
        };
        var open = () => stage.OpenAsync(binding, denial is "cancelled"
            ? new CancellationToken(canceled: true) : TestContext.Current.CancellationToken);
        if (denial is "cancelled" or "missing-live-admission")
        {
            await open.Should().ThrowAsync<OperationCanceledException>();
        }
        else if (denial is "unknown-profile")
        {
            await open.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }
        else { await open.Should().ThrowAsync<InvalidOperationException>(); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nova  Two")]
    [InlineData("Nova Two Three Four")]
    [InlineData("日本")]
    [InlineData("-")]
    public void Unsupported_or_noncanonical_English_names_are_not_replaced_with_a_default(string name)
    {
        var create = () => Profile([name]);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assets_require_exact_absolute_complete_unique_positive_hash_bound_identities()
    {
        var files = Assets();
        var relative = () => new HandsFreeNativeProfile("pocketsphinx.dll", ModelRoot, Dictionary, Vad,
            files, ["Nova"], "Synthetic metadata only");
        relative.Should().Throw<ArgumentException>();
        var missing = () => new HandsFreeNativeProfile(Native, ModelRoot, Dictionary, Vad,
            files.Take(3), ["Nova"], "Synthetic metadata only");
        missing.Should().Throw<ArgumentException>();
        var invalidHash = files.ToArray();
        invalidHash[0] = invalidHash[0] with { Sha256 = new('z', 64) };
        var malformed = () => new HandsFreeNativeProfile(Native, ModelRoot, Dictionary, Vad,
            invalidHash, ["Nova"], "Synthetic metadata only");
        malformed.Should().Throw<ArgumentException>();
        var duplicates = () => new HandsFreeNativeProfile(Native, ModelRoot, Dictionary, Vad,
            files.Concat([files[0]]), ["Nova"], "Synthetic metadata only");
        duplicates.Should().Throw<ArgumentException>();
    }

    private static HandsFreeAudioBinding Binding() =>
        new(-11, new("synthetic", "Synthetic"), "Nova", new(1, SpeechInputSelection.Default))
        {
            CanCapture = static () => true,
        };

    private static string Root => Path.GetFullPath(Path.Combine(".scratch", "native-metadata-shapes-only"));
    private static string Native => Path.Combine(Root, "native", "pocketsphinx.dll");
    private static string ModelRoot => Path.Combine(Root, "model");
    private static string Dictionary => Path.Combine(Root, "dictionary.dict");
    private static string Vad => Path.Combine(Root, "silero.onnx");
    private static HandsFreeNativeAsset[] Assets() =>
        [new(Native, 1, new('0', 64)), new(Dictionary, 1, new('0', 64)),
         new(Vad, 1, new('0', 64)), new(Path.Combine(ModelRoot, "mdef"), 1, new('0', 64))];
    private static HandsFreeNativeProfile Profile(string[]? names = null) =>
        new(Native, ModelRoot, Dictionary, Vad, Assets(), names ?? ["Nova"], "Synthetic metadata only");
}
