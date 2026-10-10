using System.Text;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalSharedSkillPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "shared-preference-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Atomic_registration_roundtrips_and_keeps_only_local_root_consent_not_content_or_grants()
    {
        var preferences = new LocalSharedSkillPreferences(new Paths(root));
        preferences.Load().Should().BeEmpty();
        var source = Source();
        preferences.Save([source]);
        preferences.Load().Should().Equal(source);
        var file = Path.Combine(root, "Preferences", "shared-skill-sources.json");
        File.ReadAllText(file).Should().NotContain("Instructions").And.NotContain("Allowed").And.NotContain("Enable");
        preferences.Save([]);
        preferences.Load().Should().BeEmpty();
        Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("{\"Version\":2,\"Sources\":[]}")]
    [InlineData("{\"Version\":\"1\",\"Sources\":[]}")]
    [InlineData("{\"Version\":1,\"Sources\":null}")]
    [InlineData("{\"Version\":1,\"Sources\":{}}")]
    [InlineData("{\"Version\":1,\"Version\":1,\"Sources\":[]}")]
    [InlineData("{\"Version\":1,\"Sources\":[],\"Enabled\":true}")]
    [InlineData("{\"Version\":1,\"Sources\":[{}]}")]
    [InlineData("{\"Version\":1,\"Sources\":[{\"Id\":\"bad\",\"ProfileRelativeRoot\":\".agents\\\\skills\",\"DirectoryIdentity\":\"a\"}]}")]
    [InlineData("{\"Version\":1,\"Sources\":[{\"Id\":null,\"ProfileRelativeRoot\":null,\"DirectoryIdentity\":null}]}")]
    public void Invalid_unknown_and_corrupt_state_is_not_reinterpreted_as_empty_consent(string text)
    {
        var store = new Store { Text = text };
        var preferences = new LocalSharedSkillPreferences(store);
        ((Action)(() => preferences.Load())).Should().Throw<InvalidDataException>();
        store.Writes.Should().Be(0);
        store.Text.Should().Be(text);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("case")]
    [InlineData("id")]
    [InlineData("alias")]
    [InlineData("count")]
    public void Duplicate_alias_case_and_oversized_registration_sets_are_rejected(string kind)
    {
        var first = Source();
        var second = kind switch
        {
            "path" => Source(),
            "case" => new SharedSkillSource(Guid.NewGuid(), ".AGENTS\\SKILLS", new string('b', 48)),
            "id" => new SharedSkillSource(first.Id, ".other\\skills", new string('b', 48)),
            "alias" => new SharedSkillSource(Guid.NewGuid(), ".other\\skills", first.DirectoryIdentity),
            _ => Source(),
        };
        var list = kind is "count" ? Enumerable.Repeat(first, 5).ToArray() : new[] { first, second };
        var preferences = new LocalSharedSkillPreferences(new Store());
        ((Action)(() => preferences.Save(list))).Should().Throw<InvalidDataException>();
        var validStore = new Store();
        var valid = new LocalSharedSkillPreferences(validStore);
        valid.Save([first]);
        var json = JsonNode.Parse(validStore.Text!)!.AsObject();
        var array = json["Sources"]!.AsArray();
        for (var index = 1; index < list.Length; index++)
        {
            var item = array[0]!.DeepClone();
            item["Id"] = list[index].Id.ToString("N");
            item["ProfileRelativeRoot"] = list[index].ProfileRelativeRoot;
            item["DirectoryIdentity"] = list[index].DirectoryIdentity;
            array.Add(item);
        }
        validStore.Text = json.ToJsonString();
        ((Action)(() => valid.Load())).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Escaped_unicode_registration_bytes_are_validated_before_replacing_existing_consent()
    {
        var store = new Store();
        var preferences = new LocalSharedSkillPreferences(store);
        var original = Source();
        preferences.Save([original]);
        var saved = store.Text;
        var oversized = Enumerable.Range(0, 4).Select(index => new SharedSkillSource(Guid.NewGuid(),
            "root-" + index + "\\" + string.Join('\\', Enumerable.Repeat(new string('漢', 70), 3)),
            new string((char)('a' + index), 48))).ToArray();
        ((Action)(() => preferences.Save(oversized))).Should().Throw<InvalidDataException>();
        store.Writes.Should().Be(1);
        store.Text.Should().Be(saved);
        preferences.Load().Should().Equal(original);
    }

    [Fact]
    public void Preference_bytes_are_bounded_before_allocation_and_strict_utf8_is_required()
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "shared-skill-sources.json");
        File.WriteAllBytes(file, [0xff]);
        var preferences = new LocalSharedSkillPreferences(new Paths(root));
        ((Action)(() => preferences.Load())).Should().Throw<InvalidDataException>();
        File.WriteAllBytes(file, new byte[4097]);
        ((Action)(() => preferences.Load())).Should().Throw<InvalidDataException>();
        var fake = new LocalSharedSkillPreferences(new Store { Text = new string('é', 2049) });
        ((Action)(() => fake.Load())).Should().Throw<InvalidDataException>();
        var store = new Store();
        ((Action)(() => new LocalSharedSkillPreferences(store).Save(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new LocalSharedSkillPreferences(store).Save([null!]))).Should().Throw<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    private static SharedSkillSource Source() => new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
    private sealed record Paths(string LocalRoot) : IApplicationDataPaths { public string RoamingRoot => LocalRoot; }

    internal sealed class Store : IPreferenceStore
    {
        internal string? Text { get; set; }
        internal int Writes { get; private set; }
        internal bool FailWrite { get; set; }
        internal bool FailRead { get; set; }
        internal Action? AfterWrite { get; set; }
        public string? ReadText(string fileName) => FailRead ? throw new IOException("read failed") : Text;
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteText(string fileName, string contents)
        {
            if (FailWrite) { throw new IOException("write failed"); }
            Text = contents;
            Writes++;
            AfterWrite?.Invoke();
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) => throw new NotSupportedException();
    }
}
