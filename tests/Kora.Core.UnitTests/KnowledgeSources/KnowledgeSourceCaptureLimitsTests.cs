using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.KnowledgeSources;

namespace Kora.Core.UnitTests.KnowledgeSources;

public sealed class KnowledgeSourceCaptureLimitsTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(32, 262144, 1048576)]
    [InlineData(2, 100, 50)]
    public void PositiveIndependentLimitsRoundTripExactly(int files, long perFile, long combined)
    {
        var limits = new KnowledgeSourceCaptureLimits(files, perFile, combined);
        KnowledgeSourceCaptureLimits.Parse(limits.Serialize()).Should().Be(limits);
        limits.MaximumFiles.Should().Be(files);
        limits.MaximumFileBytes.Should().Be(perFile);
        limits.MaximumCombinedBytes.Should().Be(combined);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(33, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 262145, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, -1)]
    [InlineData(1, 1, 1048577)]
    public void ValuesOutsideHardMaximaFailExplicitly(int files, long perFile, long combined) =>
        FluentActions.Invoking(() => new KnowledgeSourceCaptureLimits(files, perFile, combined))
            .Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("")]
    [InlineData("2:1:1:1")]
    [InlineData("1:1:1")]
    [InlineData("1:x:1:1")]
    [InlineData("1:1:x:1")]
    [InlineData("1:1:1:x")]
    [InlineData("1:01:1:1")]
    [InlineData("1:1:1:1\n")]
    [InlineData("1:1:1:1:extra")]
    [InlineData("1:0:1:1")]
    public void UnknownMalformedAndNoncanonicalSavedValuesAreNotDefaults(string text) =>
        FluentActions.Invoking(() => KnowledgeSourceCaptureLimits.Parse(text)).Should().Throw<InvalidDataException>();

    [Fact]
    public void NullSavedValueIsNotAParsedDefault() =>
        FluentActions.Invoking(() => KnowledgeSourceCaptureLimits.Parse(null!)).Should().Throw<ArgumentNullException>();

    [Theory]
    [InlineData(2, 5, 10, true)]
    [InlineData(3, 5, 15, false)]
    [InlineData(2, 6, 12, false)]
    [InlineData(2, 5, 9, false)]
    public void WholeMetadataUsesInclusiveTupleWithoutPartialAdmission(int count, long bytes, long total, bool accepted)
    {
        var limits = new KnowledgeSourceCaptureLimits(2, 5, total);
        var files = Enumerable.Range(0, count).Select(index => new LocalFileMetadata(
            $@"C:\Team\f{index}.md", $"f{index}", bytes, DateTimeOffset.UnixEpoch)).ToArray();
        KnowledgeSourceMetadata Create() => new(@"C:\Team", "root", KnowledgeSourceScope.ImmediateFiles,
            [new(@"C:\Team", "root")], files, limits);
        if (!accepted) { FluentActions.Invoking(Create).Should().Throw<InvalidDataException>(); return; }
        var metadata = Create();
        metadata.Files.Should().HaveCount(count);
        metadata.CombinedBytes.Should().Be(total);
        metadata.CaptureLimits.Should().Be(limits);
        metadata.Matches(new(@"C:\Team", "root", metadata.Scope, metadata.Directories, files, limits)).Should().BeTrue();
        metadata.Matches(new(@"C:\Team", "root", metadata.Scope, metadata.Directories, files)).Should().BeFalse();
    }

    [Fact]
    public async Task LegacyInspectorRemainsDefaultOnlyRatherThanPretendingToEnforceLowerLimits()
    {
        IKnowledgeSourceInspector inspector = new LegacyInspector();
        using var selection = await inspector.InspectSourceAsync(@"C:\Team", KnowledgeSourceScope.ImmediateFiles,
            KnowledgeSourceCaptureLimits.HardMaximum, TestContext.Current.CancellationToken);
        selection.Metadata.CaptureLimits.Should().Be(KnowledgeSourceCaptureLimits.HardMaximum);
        await FluentActions.Awaiting(() => inspector.InspectSourceAsync(@"C:\Team", KnowledgeSourceScope.ImmediateFiles,
            new(1, 1, 1), TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class LegacyInspector : IKnowledgeSourceInspector, IKnowledgeSourceSelection
    {
        public KnowledgeSourceMetadata Metadata { get; } = new(@"C:\Team", "root", KnowledgeSourceScope.ImmediateFiles,
            [new(@"C:\Team", "root")], [new(@"C:\Team\f.md", "f", 0, DateTimeOffset.UnixEpoch)]);
        public Task<IKnowledgeSourceSelection> InspectSourceAsync(string selectedPath, KnowledgeSourceScope scope,
            CancellationToken cancellationToken) => Task.FromResult<IKnowledgeSourceSelection>(this);
        public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
        public void Dispose() { }
    }
}
