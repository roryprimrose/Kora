using System.Security.AccessControl;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class RestrictedStorageDirectoryTests
{
    [WindowsFact]
    public void Foreign_owner_is_rejected_even_with_a_protected_full_control_rule_for_the_current_user()
    {
        using var fixture = new OwnedStorageFixture();
        using var identity = WindowsIdentity.GetCurrent();
        var directory = new RestrictedStorageDirectory(fixture);
        var descriptor = new FileSecurity();
        descriptor.SetOwner(new SecurityIdentifier(WellKnownSidType.WorldSid, null));
        descriptor.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        descriptor.AddAccessRule(new(identity.User ?? throw new InvalidOperationException("The fixture profile is unavailable."),
            FileSystemRights.FullControl, AccessControlType.Allow));
        var before = descriptor.GetSecurityDescriptorBinaryForm();
        // Exercise the production owner policy in memory without changing an OS owner or requiring privilege.
        var verify = () => directory.VerifyPermissions(descriptor, requireProtected: true, allowSystemAdministrators: false);
        verify.Should().Throw<UnauthorizedAccessException>();
        descriptor.GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public void Newly_created_lease_and_content_files_have_explicit_private_current_user_security()
    {
        using var fixture = new OwnedStorageFixture();
        using var identity = WindowsIdentity.GetCurrent();
        var directory = new RestrictedStorageDirectory(fixture);
        directory.CreateNew();
        using (directory.AcquireLease())
        {
            AssertPrivateFile(Path.Combine(directory.Root, "operation.lock"), identity.User);
        }
        var path = Path.Combine(directory.Artifacts, "synthetic.pending");
        using (var stream = directory.CreateNewFile(path, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            stream.IsAsync.Should().BeTrue();
            stream.Length.Should().Be(0);
            AssertPrivateFile(path, identity.User);
        }
        using (directory.AcquireLease())
        {
            AssertPrivateFile(Path.Combine(directory.Root, "operation.lock"), identity.User);
        }
    }

    [WindowsFact]
    public void Create_new_cannot_replace_an_existing_file_or_repair_its_permissions()
    {
        using var fixture = new OwnedStorageFixture();
        var directory = new RestrictedStorageDirectory(fixture);
        directory.CreateNew();
        var path = Path.Combine(directory.Artifacts, "synthetic.pending");
        using (var stream = directory.CreateNewFile(path))
        {
            stream.Write("preserved synthetic bytes"u8);
        }
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(security);
        var before = file.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var create = () => directory.CreateNewFile(path);
        create.Should().Throw<IOException>();
        File.ReadAllBytes(path).Should().Equal("preserved synthetic bytes"u8.ToArray());
        file.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public void Existing_permissive_lease_is_rejected_without_repair()
    {
        using var fixture = new OwnedStorageFixture();
        var directory = new RestrictedStorageDirectory(fixture);
        directory.CreateNew();
        using (directory.AcquireLease())
        {
        }
        var file = new FileInfo(Path.Combine(directory.Root, "operation.lock"));
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(security);
        var before = file.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var acquire = directory.AcquireLease;
        acquire.Should().Throw<UnauthorizedAccessException>();
        file.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public void New_file_creation_cannot_escape_the_owned_partition()
    {
        using var fixture = new OwnedStorageFixture();
        var directory = new RestrictedStorageDirectory(fixture);
        directory.CreateNew();
        var path = Path.Combine(fixture.LocalRoot, "outside.pending");
        var create = () => directory.CreateNewFile(path);
        create.Should().Throw<UnauthorizedAccessException>();
        File.Exists(path).Should().BeFalse();
    }

    private static void AssertPrivateFile(string path, SecurityIdentifier? user)
    {
        var security = new FileInfo(path).GetAccessControl();
        security.GetOwner(typeof(SecurityIdentifier)).Should().Be(user);
        security.AreAccessRulesProtected.Should().BeTrue();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToArray();
        rules.Should().ContainSingle();
        rules[0].IdentityReference.Should().Be(user);
        rules[0].AccessControlType.Should().Be(AccessControlType.Allow);
        rules[0].FileSystemRights.Should().Be(FileSystemRights.FullControl);
        rules[0].IsInherited.Should().BeFalse();
    }
}
