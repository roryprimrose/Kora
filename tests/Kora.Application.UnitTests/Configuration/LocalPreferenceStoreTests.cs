using System.Globalization;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalPreferenceStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.PreferenceStoreTests.{Guid.NewGuid():N}");

    [Fact]
    public void Read_returns_null_when_the_preference_does_not_exist()
    {
        var store = CreateStore();

        store.ReadText("missing.txt").Should().BeNull();
        store.ReadLines("missing.txt").Should().BeNull();
    }

    [Fact]
    public void Write_replaces_existing_content_without_leaving_temporary_files()
    {
        var store = CreateStore();

        store.WriteText("value.txt", "first");
        store.WriteText("value.txt", "second");

        store.ReadText("value.txt").Should().Be("second");
        Directory.GetFiles(
            Path.Combine(root, "Preferences"),
            "*.tmp",
            SearchOption.TopDirectoryOnly).Should().BeEmpty();
    }

    [Fact]
    public void WriteLines_and_delete_use_the_same_preference_boundary()
    {
        var store = CreateStore();

        store.WriteLines("values.txt", ["first", "second"]);

        store.ReadLines("values.txt").Should().Equal("first", "second");
        store.Delete("values.txt");
        store.ReadText("values.txt").Should().BeNull();
    }

    [Fact]
    public void Concurrent_writes_do_not_collide_or_leave_partial_files()
    {
        var store = CreateStore();

        Parallel.For(
            0,
            20,
            value => store.WriteText(
                "value.txt",
                value.ToString(CultureInfo.InvariantCulture)));

        int.Parse(
            store.ReadText("value.txt")!,
            CultureInfo.InvariantCulture).Should().BeInRange(0, 19);
        Directory.GetFiles(
            Path.Combine(root, "Preferences"),
            "*.tmp",
            SearchOption.TopDirectoryOnly).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("nested/value.txt")]
    [InlineData(@"nested\value.txt")]
    [InlineData(@"C:value.txt")]
    [InlineData("..")]
    public void Operations_reject_paths_outside_the_preference_directory(string fileName)
    {
        var action = () => CreateStore().WriteText(fileName, "value");

        action.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalPreferenceStore CreateStore() =>
        new(new TestPaths(root));

    private sealed record TestPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}