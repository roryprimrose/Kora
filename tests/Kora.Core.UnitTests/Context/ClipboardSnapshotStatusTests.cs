using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class ClipboardSnapshotStatusTests
{
    private const string Id = "01234567-89ab-cdef-0123-456789abcdef";

    [Theory]
    [InlineData("clipboard snapshot status ", false)]
    [InlineData("Nova Prime, clipboard snapshot status ", true)]
    [InlineData("NOVA PRIME clipboard snapshot status ", true)]
    public void Exact_id_and_current_activation_are_preserved(string prefix, bool activated)
    {
        var command = ClipboardSnapshotStatusCommand.Parse(prefix + Id, "Nova Prime")!;
        command.IsValid.Should().BeTrue();
        command.IsActivated.Should().Be(activated);
        command.IsHelp.Should().BeFalse();
        command.SnapshotId.Should().Be(Guid.Parse(Id));
    }

    [Theory]
    [InlineData("clipboard snapshot help")]
    [InlineData("Nova Prime, clipboard snapshot help")]
    public void Help_has_no_snapshot_identity(string input)
    {
        var command = ClipboardSnapshotStatusCommand.Parse(input, "Nova Prime")!;
        command.IsValid.Should().BeTrue();
        command.IsHelp.Should().BeTrue();
        command.SnapshotId.Should().BeEmpty();
        ClipboardSnapshotStatusCommand.Syntax.Should().NotContain(Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("clipboard find id literal")]
    [InlineData("clipboard snapshots help")]
    [InlineData("clipboardx snapshot help")]
    [InlineData("Nova Prime")]
    [InlineData("Nova Primex, clipboard snapshot help")]
    [InlineData("Kora, clipboard snapshot help")]
    [InlineData("ordinary question")]
    public void Unrelated_commands_are_not_claimed(string input) =>
        ClipboardSnapshotStatusCommand.Parse(input, "Nova Prime").Should().BeNull();

    [Theory]
    [InlineData("clipboard snapshot")]
    [InlineData("clipboard snapshot status")]
    [InlineData("clipboard snapshot help extra")]
    [InlineData(" clipboard snapshot help")]
    [InlineData("clipboard\tsnapshot help")]
    [InlineData("clipboard.snapshot help")]
    [InlineData("clipboard\u200bsnapshot help")]
    [InlineData("Nova Prime\tclipboard snapshot help")]
    [InlineData(" Nova Prime, clipboard snapshot help")]
    [InlineData("Nova Prime\u200bclipboard snapshot help")]
    [InlineData("clipboard snapshot status 00000000-0000-0000-0000-000000000000")]
    [InlineData("clipboard snapshot status 0123456789abcdef0123456789abcdef")]
    [InlineData("clipboard snapshot status {01234567-89ab-cdef-0123-456789abcdef}")]
    [InlineData("clipboard snapshot status 01234567-89AB-CDEF-0123-456789ABCDEF")]
    [InlineData("clipboard snapshot status 01234567-89ab-cdef-0123-456789abcdef ")]
    [InlineData("clipboard snapshot status  01234567-89ab-cdef-0123-456789abcdef")]
    [InlineData("clipboard snapshot status 01234567\u201189ab-cdef-0123-456789abcdef")]
    [InlineData("clipboard snapshot status 01234567-89ab-cdef-0123-456789abcdeg")]
    [InlineData("clipboard snapshot status 01234567-89ab-cdef-0123-456789abcde\ud800")]
    public void Malformed_reserved_input_is_not_repaired(string input)
    {
        var command = ClipboardSnapshotStatusCommand.Parse(input, "Nova Prime")!;
        command.IsValid.Should().BeFalse();
        command.SnapshotId.Should().BeEmpty();
    }

    [Fact]
    public void Overlong_reserved_input_is_rejected_without_echo()
    {
        var command = ClipboardSnapshotStatusCommand.Parse("clipboard snapshot status " + new string('a', 10000), "Kora")!;
        command.IsValid.Should().BeFalse();
        command.ToString().Should().NotContain(new string('a', 50));
    }

    [Fact]
    public void Metadata_only_policy_requires_the_exact_current_host_id()
    {
        var snapshot = new ClipboardSnapshot(Guid.NewGuid(), Guid.Parse(Id), HostRequest.Create(RequestOrigin.LocalUi),
            "private body", 1, DateTimeOffset.UnixEpoch);
        var present = ClipboardSnapshotStatusResult.FromCurrent(Guid.Parse(Id), snapshot);
        present.IsPresent.Should().BeTrue();
        present.SnapshotId.Should().Be(snapshot.SnapshotId);
        present.Utf8Bytes.Should().Be(snapshot.Utf8Bytes);
        present.ToString().Should().NotContain(snapshot.Text);
        foreach (var result in new[]
        {
            ClipboardSnapshotStatusResult.FromCurrent(Guid.NewGuid(), snapshot),
            ClipboardSnapshotStatusResult.FromCurrent(Guid.Empty, snapshot),
            ClipboardSnapshotStatusResult.FromCurrent(Guid.Parse(Id), null),
        })
        {
            result.Should().BeSameAs(ClipboardSnapshotStatusResult.Unavailable);
            result.IsPresent.Should().BeFalse();
            result.SnapshotId.Should().BeNull();
            result.Utf8Bytes.Should().BeNull();
        }
    }
}
