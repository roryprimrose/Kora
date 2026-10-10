using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Network;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalPreapprovedUriPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(
        Environment.CurrentDirectory,
        ".net-test-artifacts",
        $"preapproved-uri-preferences-{Guid.NewGuid():N}");
    private string DirectoryPath => Path.Combine(root, "Preferences");
    private string PreferencePath => Path.Combine(DirectoryPath, "preapproved-uris.txt");
    private LocalPreapprovedUriPreferences Create() => new(new Paths(root, root));

    [Fact]
    public void MissingStorageIsEmptyWithoutCreatingFiles()
    {
        Create().Load().Should().Be(PreapprovedUriSettings.Empty);
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void AtomicReplacementAndRestartReadBackCanonicalPatterns()
    {
        var preferences = Create();
        preferences.Save(PreapprovedUriSettings.Create(
            ["https://*.example.com/*", "HTTPS://münich.example:443"]));

        Create().Load().Patterns.Should().Equal(
            "https://*.example.com/*",
            "https://xn--mnich-kva.example/");
        File.ReadAllLines(PreferencePath).Should().Equal(
            "1",
            "https://*.example.com/*",
            "https://xn--mnich-kva.example/");
        Directory.GetFiles(DirectoryPath, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("1\nftp://example.com/")]
    [InlineData("1\nhttps://foo*.example.com/")]
    [InlineData("1\nhttps://example.com/\nhttps://example.com/")]
    public void MalformedSavedStateNeverBecomesAnEmptyAllowlist(string text)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(PreferencePath, text);

        Create().Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
        File.ReadAllText(PreferencePath).Should().Be(text);
    }

    [Fact]
    public void InvalidUtf8AndOversizedStateAreRejected()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllBytes(PreferencePath, [0xff]);
        Create().Invoking(value => value.Load()).Should().Throw<InvalidDataException>();

        File.WriteAllBytes(
            PreferencePath,
            new byte[PreapprovedUriPattern.MaximumTotalUtf8Bytes
                + PreapprovedUriPattern.MaximumPatternCount * Environment.NewLine.Length
                + 3]);
        Create().Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
