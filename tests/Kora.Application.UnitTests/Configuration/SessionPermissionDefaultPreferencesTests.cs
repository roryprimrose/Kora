using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SessionPermissionDefaultPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts",
        "session-permission-default-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(SessionPermissionLevel.ReviewAll)]
    [InlineData(SessionPermissionLevel.ModelReview)]
    [InlineData(SessionPermissionLevel.ApproveAll)]
    public void DeviceDefaultRoundtripsWithoutChangingOtherPreferences(SessionPermissionLevel level)
    {
        var paths = new Paths(root);
        var store = new LocalPreferenceStore(paths);
        store.WriteText("other.txt", "unchanged");
        var preferences = new SessionPermissionDefaultPreferences(store);
        preferences.Load().Should().Be(SessionPermissionLevel.ReviewAll);
        preferences.Save(level);
        new SessionPermissionDefaultPreferences(paths).Load().Should().Be(level);
        store.ReadText("other.txt").Should().Be("unchanged");
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ApproveAll")]
    [InlineData("approve-all\n")]
    [InlineData("unknown")]
    public void MalformedSavedDefaultIsRejectedInsteadOfReinterpreted(string value)
    {
        var store = new LocalPreferenceStore(new Paths(root));
        store.WriteText("session-permission-default.txt", value);
        new SessionPermissionDefaultPreferences(store).Invoking(preferences => preferences.Load())
            .Should().Throw<InvalidDataException>();
        new SessionPermissionDefaultPreferences(store).Invoking(preferences => preferences.Save((SessionPermissionLevel)99))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }
}
