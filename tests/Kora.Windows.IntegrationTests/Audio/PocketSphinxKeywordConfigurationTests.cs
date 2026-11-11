using System.Text;

using AwesomeAssertions;

using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class PocketSphinxKeywordConfigurationTests
{
    [Theory]
    [InlineData("kora K AO R AH\n", "Kora", "kora")]
    [InlineData(";; comment\n## comment\n\nKoRa K AO R AH\n", "Kora", "KoRa")]
    [InlineData("hello HH EH L OW\nkora K AO R AH\n", "Kora Hello", "kora hello")]
    [InlineData("kora(2) K AO R AH\nkora K AO R AH\n", "Kora", "kora")]
    public async Task MapsOnlyLiteralWordsAndPreservesDictionaryCase(string dictionary, string name, string expected)
    {
        var profile = await WriteDictionary(dictionary, name);
        var configuration = await PocketSphinxKeywordConfiguration.CreateAsync(profile, name, TestContext.Current.CancellationToken);
        configuration.Profile.Should().BeSameAs(profile);
        configuration.AssistantName.Should().Be(name);
        configuration.Keyphrase.Should().Be(expected);
    }

    [Theory]
    [InlineData("nova N OW V AH\n")]
    [InlineData("kora(2) K AO R AH\n")]
    [InlineData("korah K AO R AH\n")]
    public async Task MissingLiteralNameHasNoSpellingOrPronunciationFallback(string dictionary)
    {
        var profile = await WriteDictionary(dictionary);
        var create = () => PocketSphinxKeywordConfiguration.CreateAsync(profile, "Kora", TestContext.Current.CancellationToken);
        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*absent*");
    }

    [Theory]
    [InlineData("kora K AO R AH\nKORA K AO R AH\n")]
    [InlineData("kora\n")]
    [InlineData("kora K-AO\n")]
    [InlineData("kora K\0\n")]
    [InlineData("kora K Å\n")]
    [InlineData("nova\nkora K AO R AH\n")]
    public async Task RejectsAmbiguityAndMalformedEntriesWithoutPartialMapping(string dictionary)
    {
        var profile = await WriteDictionary(dictionary);
        var create = () => PocketSphinxKeywordConfiguration.CreateAsync(profile, "Kora", TestContext.Current.CancellationToken);
        await create.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("bytes")]
    [InlineData("lines")]
    [InlineData("line")]
    [InlineData("token")]
    [InlineData("tokens")]
    [InlineData("utf8")]
    [InlineData("bom")]
    public async Task RejectsStrictDecodingGrammarAndResourceBoundViolations(string violation)
    {
        var contents = violation switch
        {
            "empty" => [],
            "bytes" => new byte[4 * 1024 * 1024 + 1],
            "lines" => Encoding.ASCII.GetBytes("kora K\n" + string.Concat(Enumerable.Repeat("\n", 200000))),
            "line" => Encoding.ASCII.GetBytes("##" + new string('a', 1023) + "\nkora K\n"),
            "token" => Encoding.ASCII.GetBytes(new string('a', 65) + " K\nkora K\n"),
            "tokens" => Encoding.ASCII.GetBytes("kora " + string.Join(' ', Enumerable.Repeat("K", 65))),
            "utf8" => [0x6B, 0x6F, 0x72, 0x61, 0x20, 0xC3, 0x28],
            "bom" => [0xEF, 0xBB, 0xBF, 0x6B, 0x6F, 0x72, 0x61, 0x20, 0x4B],
            _ => throw new ArgumentOutOfRangeException(nameof(violation)),
        };
        var profile = SyntheticProfile();
        await File.WriteAllBytesAsync(profile.DictionaryPath, contents, TestContext.Current.CancellationToken);
        var create = () => PocketSphinxKeywordConfiguration.CreateAsync(profile, "Kora", TestContext.Current.CancellationToken);
        await create.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("line")]
    [InlineData("lines")]
    [InlineData("token")]
    [InlineData("tokens")]
    [InlineData("bytes")]
    public async Task AcceptsExactGrammarBounds(string boundary)
    {
        var dictionary = boundary switch
        {
            "line" => "##" + new string('a', 1022) + "\nkora K\n",
            "lines" => "kora K\n" + string.Concat(Enumerable.Repeat("\n", 199999)),
            "token" => new string('a', 64) + " K\nkora K\n",
            "tokens" => "kora " + string.Join(' ', Enumerable.Repeat("K", 64)),
            "bytes" => "kora K\n" + string.Concat(Enumerable.Repeat("##" + new string('a', 1021) + "\n", 4095))
                + "##" + new string('a', 1015),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary)),
        };
        var profile = await WriteDictionary(dictionary);
        if (boundary is "bytes") { new FileInfo(profile.DictionaryPath).Length.Should().Be(4 * 1024 * 1024); }
        (await PocketSphinxKeywordConfiguration.CreateAsync(profile, "Kora", TestContext.Current.CancellationToken))
            .Keyphrase.Should().Be("kora");
    }

    [Theory]
    [InlineData("Nova", true)]
    [InlineData(" Kora", true)]
    [InlineData("Kora", false)]
    public async Task RejectsUnadmittedNamesBeforeOpeningDictionary(string name, bool qualified)
    {
        var profile = SyntheticProfile(qualified: qualified);
        File.Exists(profile.DictionaryPath).Should().BeFalse();
        var create = () => PocketSphinxKeywordConfiguration.CreateAsync(profile, name, TestContext.Current.CancellationToken);
        await create.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(profile.DictionaryPath).Should().BeFalse();
    }

    [Fact]
    public async Task CancellationDoesNotProduceAConfiguration()
    {
        var profile = await WriteDictionary("kora K AO R AH\n");
        var create = () => PocketSphinxKeywordConfiguration.CreateAsync(profile, "Kora", new CancellationToken(canceled: true));
        await create.Should().ThrowAsync<OperationCanceledException>();
    }

    internal static HandsFreeNativeProfile SyntheticProfile(string name = "Kora", bool qualified = true)
    {
        var root = HandsFreeOfflineFixture.CreateScratchDirectory();
        var native = Path.Combine(root, "never-load.dll");
        var dictionary = Path.Combine(root, "dictionary.dict");
        var vad = Path.Combine(root, "never-load.onnx");
        return new(native, root, dictionary, vad,
            [new(native, 1, new('0', 64)), new(dictionary, 1, new('0', 64)),
             new(vad, 1, new('0', 64)), new(Path.Combine(root, "mdef"), 1, new('0', 64))],
            [name], qualified ? "Synthetic tests only; not acoustic qualification" : null);
    }

    private static async Task<HandsFreeNativeProfile> WriteDictionary(string dictionary, string name = "Kora")
    {
        var profile = SyntheticProfile(name: name);
        await File.WriteAllTextAsync(profile.DictionaryPath, dictionary, new UTF8Encoding(false), TestContext.Current.CancellationToken);
        return profile;
    }
}
