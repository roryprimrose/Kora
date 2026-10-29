using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalBundledRuntimeRegistrationPreferencesTests
{
    [Fact]
    public void Preparation_and_forgetting_roundtrip_through_the_shared_atomic_store()
    {
        var store = new Store();
        var preferences = new LocalBundledRuntimeRegistrationPreferences(store);
        preferences.Load().Should().BeNull();
        preferences.LoadPending().Should().BeNull();
        var value = new BundledRuntimeRegistration(new('a', 64), new('b', 64), false);
        preferences.Save(value);
        preferences.Load().Should().Be(value);
        store.Name.Should().Be("bundled-runtime-registration.txt");
        preferences.Save(null);
        preferences.Load().Should().BeNull();
        preferences.SavePending(value);
        preferences.LoadPending().Should().Be(value);
        preferences.SavePending(null);
        preferences.LoadPending().Should().BeNull();
    }

    [Fact]
    public void Invalid_saved_metadata_is_preserved_and_reported()
    {
        var store = new Store { Text = "invalid" };
        var preferences = new LocalBundledRuntimeRegistrationPreferences(store);
        ((Action)(() => preferences.Load())).Should().Throw<InvalidDataException>();
        store.Text.Should().Be("invalid");
        store.Text = new string('a', 257);
        ((Action)(() => preferences.Load())).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Production_constructor_uses_device_local_paths()
    {
        var paths = new Paths(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "runtime-registration-" + Guid.NewGuid().ToString("N")));
        var preferences = new LocalBundledRuntimeRegistrationPreferences(paths);
        preferences.Load().Should().BeNull();
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths { public string RoamingRoot => throw new InvalidOperationException(); }
    private sealed class Store : IPreferenceStore
    {
        internal string? Text { get; set; }
        internal string? Name { get; private set; }
        public string? ReadText(string fileName) { Name = fileName; return Text; }
        public void WriteText(string fileName, string contents) { Name = fileName; Text = contents; }
        public void Delete(string fileName) { Name = fileName; Text = null; }
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
    }
}
