using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileTests
{
    [Theory]
    [InlineData("file.txt")]
    [InlineData("")]
    [InlineData("C:")]
    [InlineData(@"C:\folder")]
    [InlineData(@"C:file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\?\C:\file.txt")]
    [InlineData(@"C:\%USERPROFILE%\file.txt")]
    [InlineData(@"C:\folder\..\file.txt")]
    [InlineData(@"C:\folder\.\file.txt")]
    [InlineData(@"C:\folder\\file.txt")]
    [InlineData(@"C:\folder.\file.txt")]
    [InlineData(@"C:\folder \file.txt")]
    [InlineData(@"C:\file.txt:secret")]
    [InlineData(@"C:/file.txt")]
    [InlineData(@"C:\NUL.txt")]
    [InlineData(@"C:\COM1.txt")]
    [InlineData(@"C:\LPT9\file.txt")]
    [InlineData(@"C:\CON.txt")]
    [InlineData(@"C:\PRN.md")]
    [InlineData(@"C:\AUX.md")]
    [InlineData(@"C:\CONIN$.txt")]
    [InlineData(@"C:\CONOUT$.txt")]
    [InlineData(@"1:\file.txt")]
    [InlineData(@"C;\file.txt")]
    [InlineData("C:\\invalid\tname.txt")]
    [InlineData(@"C:\invalid|name.txt")]
    [InlineData("C:\\Team\\guide\u202e.txt")]
    [InlineData(@"C:\.git\config.txt")]
    [InlineData(@"C:\.ssh\identity.txt")]
    [InlineData(@"C:\node_modules\package.md")]
    [InlineData(@"C:\obj\secrets.txt")]
    [InlineData(@"C:\source.cs")]
    [InlineData(@"C:\document.md.exe")]
    [InlineData(@"C:\folder\")]
    public void Hostile_or_noncanonical_paths_are_denied(string path)
    {
        var validate = () => LocalFilePolicy.ValidatePath(path);
        validate.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(@"C:\Team\notes.txt")]
    [InlineData(@"D:\Team\Guide.MD")]
    [InlineData(@"C:\Team\日本語.markdown")]
    [InlineData(@"C:\Team\LPT0.txt")]
    [InlineData(@"C:\Team\COMA.txt")]
    [InlineData(@"C:\Team\AXY1.txt")]
    public void Canonical_supported_path_is_allowed(string path) =>
        LocalFilePolicy.ValidatePath(path);

    [Fact]
    public void Depth_and_path_length_are_bounded_and_root_prefixes_do_not_alias()
    {
        var deep = () => LocalFilePolicy.ValidatePath(@"C:\" + string.Join("\\", Enumerable.Repeat("a", LocalFilePolicy.MaximumDepth)) + @"\f.txt");
        deep.Should().Throw<InvalidDataException>();
        var longPath = () => LocalFilePolicy.ValidatePath(@"C:\" + new string('a', LocalFilePolicy.MaximumPathCharacters) + ".txt");
        longPath.Should().Throw<InvalidDataException>();
        LocalFilePolicy.IsWithin(@"C:\Kora\private.txt", @"c:\kora").Should().BeTrue();
        LocalFilePolicy.IsWithin(@"C:\Korax\private.txt", @"C:\Kora").Should().BeFalse();
        LocalFilePolicy.IsWithin(@"C:\Kora", @"C:\Kora\").Should().BeTrue();
    }

    [Fact]
    public void Strict_utf8_preserves_exact_text_and_hashes_original_bytes_including_BOM()
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("# héllo\r\n🙂\t ")];
        var review = new LocalFileReview(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\guide.md", "fixed-identity", bytes.Length, DateTimeOffset.UnixEpoch));
        var revision = new LocalFileRevision(review, bytes, DateTimeOffset.UnixEpoch);
        revision.Text.Should().Be("# héllo\r\n🙂\t ");
        revision.Digest.Should().Be(LocalFilePolicy.Digest(bytes));
        revision.Digest.Should().NotBe(LocalFilePolicy.Digest(bytes.AsSpan(3)));
        revision.Review.Should().BeSameAs(review);
        revision.RevisionId.Should().NotBeEmpty();
        revision.ItemId.Should().NotBeEmpty();
        revision.AdmittedAt.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Theory]
    [InlineData(new byte[] { 0xc0, 0xaf })]
    [InlineData(new byte[] { 0xed, 0xa0, 0x80 })]
    [InlineData(new byte[] { 0xf0, 0x9f })]
    [InlineData(new byte[] { 0xff, 0xfe, 0x41, 0 })]
    public void Malformed_or_non_utf8_is_never_replaced_or_guessed(byte[] bytes)
    {
        var decode = () => LocalFilePolicy.Decode(bytes);
        decode.Should().Throw<DecoderFallbackException>();
    }

    [Theory]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0x1b, 0x5b })]
    [InlineData(new byte[] { 0x01 })]
    public void Binary_or_control_text_is_not_admitted(byte[] bytes)
    {
        var decode = () => LocalFilePolicy.Decode(bytes);
        decode.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Byte_boundary_is_inclusive_and_no_truncation_occurs()
    {
        LocalFilePolicy.Decode(Array.Empty<byte>()).Should().BeEmpty();
        var bytes = Encoding.UTF8.GetBytes(new string('é', LocalFilePolicy.MaximumBytes / 2));
        LocalFilePolicy.Decode(bytes).Length.Should().Be(LocalFilePolicy.MaximumBytes / 2);
        var decode = () => LocalFilePolicy.Decode([.. bytes, 0x61]);
        decode.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Exact_commands_accept_neither_paths_nor_confirmations()
    {
        LocalFileCommand.Parse("preview file")!.Operation.Should().Be(LocalFileOperation.Select);
        LocalFileCommand.Parse("clear file preview")!.Operation.Should().Be(LocalFileOperation.Clear);
        foreach (var text in new[] { "preview file c team file txt", "confirm file", "preview folder", "preview file now" })
        {
            LocalFileCommand.Parse(text).Should().BeNull();
        }
    }

    [Theory]
    [InlineData("review")]
    [InlineData("source")]
    [InlineData("identity")]
    [InlineData("length")]
    public void Revision_rejects_unbound_identity_or_changed_length(string invalid)
    {
        var review = new LocalFileReview(string.Equals(invalid, "review", StringComparison.Ordinal) ? Guid.Empty : Guid.NewGuid(),
            string.Equals(invalid, "source", StringComparison.Ordinal) ? Guid.Empty : Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\guide.txt", string.Equals(invalid, "identity", StringComparison.Ordinal) ? "" : "identity",
                string.Equals(invalid, "length", StringComparison.Ordinal) ? 2 : 1, DateTimeOffset.UnixEpoch));
        var create = () => new LocalFileRevision(review, [0x61], DateTimeOffset.UnixEpoch);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Malformed_unicode_path_cannot_be_replaced_into_another_selection()
    {
        var path = @"C:\Team\guide" + new string((char)0xd800, 1) + ".txt";
        var validate = () => LocalFilePolicy.ValidatePath(path);
        validate.Should().Throw<InvalidDataException>();
    }
}
