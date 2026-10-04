using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalModelApprovalPreferencesTests
{
    [Fact]
    public void Approval_preferences_default_to_requiring_the_assistant_name()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kora-approvals-{Guid.NewGuid():N}");
        var preferences = new LocalModelApprovalPreferences(new TestPaths(path));

        var result = preferences.Load();

        result.RequireAssistantNameForVoiceApproval.Should().BeTrue();
        result.AlwaysAllowedActions.Should().BeEmpty();
    }

    [Fact]
    public void Always_approved_actions_and_voice_setting_survive_a_new_instance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kora-approvals-{Guid.NewGuid():N}");
        try
        {
            new LocalModelApprovalPreferences(new TestPaths(path)).Save(
                new ModelApprovalPreferences(false, [BuiltInAction.LockMachine]));

            var result = new LocalModelApprovalPreferences(new TestPaths(path)).Load();

            result.RequireAssistantNameForVoiceApproval.Should().BeFalse();
            result.AlwaysAllowedActions.Should().ContainSingle()
                .Which.Should().Be(BuiltInAction.LockMachine);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("""{"Version":1,"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":["Unknown"]}""")]
    [InlineData("""{"Version":1,"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":["LockMachine","LockMachine"]}""")]
    [InlineData("""{"Version":2,"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":[]}""")]
    [InlineData("""{"Version":1,"AlwaysAllowedActions":[]}""")]
    [InlineData("""{"Version":1,"AlwaysAllowedActions":["LockMachine"]""")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":[]}""")]
    [InlineData("""{"Version":"1","RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":[]}""")]
    [InlineData("""{"Version":1.5,"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":[]}""")]
    [InlineData("""{"Version":1,"RequireAssistantNameForVoiceApproval":null,"AlwaysAllowedActions":[]}""")]
    [InlineData("""{"Version":1,"RequireAssistantNameForVoiceApproval":true}""")]
    [InlineData("""{"Version":1,"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":null}""")]
    [InlineData("""{"Version":1,"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":[1]}""")]
    [InlineData("""{"Version":1,"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":["9999"]}""")]
    [InlineData("""{"Version":1,"RequireAssistantNameForVoiceApproval":true,"AlwaysAllowedActions":["lockmachine"]}""")]
    public void Invalid_or_newer_grants_are_never_accepted(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kora-approvals-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(path, "Preferences"));
            File.WriteAllText(Path.Combine(path, "Preferences", "model-approvals.json"), json);

            var action = () => new LocalModelApprovalPreferences(new TestPaths(path)).Load();

            action.Should().Throw<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void Save_rejects_null_preferences_and_invalid_grants()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kora-approvals-{Guid.NewGuid():N}");
        var preferences = new LocalModelApprovalPreferences(new TestPaths(path));

        var nullPreferences = () => preferences.Save(null!);
        var nullGrants = () => preferences.Save(new ModelApprovalPreferences(true, null!));
        var unknownGrant = () => preferences.Save(new ModelApprovalPreferences(true, [(BuiltInAction)9999]));
        var duplicateGrant = () => preferences.Save(
            new ModelApprovalPreferences(true, [BuiltInAction.LockMachine, BuiltInAction.LockMachine]));

        nullPreferences.Should().Throw<ArgumentNullException>();
        nullGrants.Should().Throw<ArgumentException>();
        unknownGrant.Should().Throw<ArgumentException>();
        duplicateGrant.Should().Throw<ArgumentException>();
        Directory.Exists(path).Should().BeFalse();
    }

    private sealed record TestPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}
