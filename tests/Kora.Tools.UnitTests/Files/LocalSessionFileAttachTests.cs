using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Files;

[Collection("Host tracing")]
public sealed class LocalSessionFileAttachTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public LocalSessionFileAttachTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData("")]
    [InlineData("# exact café\r\n🙂 marker")]
    public async Task CapturePersistsAuthoritativeOriginalBytesAfterNativeReleaseThenClearsOwnedBuffer(string text)
    {
        byte[] original = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)];
        var selection = new Selection(original);
        var inspector = new Inspector(selection);
        await using var action = new LocalSessionFileAttach(inspector, new Audit(),
            TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        (await action.Select(new Picker(), () => true, TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Reviewed);
        var review = action.Review!;
        action.IsQuiescent.Should().BeFalse();
        var digest = LocalFilePolicy.Digest(original);
        var calls = 0;
        (await action.Execute(review.ReviewId, () => true, (file, bytes, _) =>
        {
            selection.Disposals.Should().Be(1);
            calls++;
            bytes.ToArray().Should().Equal(original);
            file.Digest.Should().Be(digest);
            file.Text.Should().Be(text);
            file.Review.Should().BeSameAs(review);
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Admitted);
        calls.Should().Be(1);
        original.Should().OnlyContain(value => value == 0);
        inspector.Calls.Should().Be(1);
        action.IsQuiescent.Should().BeTrue();
        action.Revoke();
        action.Review.Should().BeNull();
        await action.WaitForQuiescence();
        await action.Clear();
    }

    [Theory]
    [InlineData("release")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    public async Task NativeReleaseOrCurrentAdmissionFailureCannotInvokePersistence(string failure)
    {
        var eligible = true;
        var selection = new Selection([(byte)'a'])
        {
            BeforeRead = () =>
            {
                if (string.Equals(failure, "cancel", StringComparison.Ordinal))
                {
                    throw new OperationCanceledException(TestContext.Current.CancellationToken);
                }
                if (string.Equals(failure, "privacy", StringComparison.Ordinal)) { eligible = false; }
            },
            ReleaseFailure = string.Equals(failure, "release", StringComparison.Ordinal),
        };
        var action = new LocalSessionFileAttach(new Inspector(selection), new Audit(),
            TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await action.Select(new Picker(), () => eligible, TestContext.Current.CancellationToken);
        var persisted = false;
        var execute = () => action.Execute(action.Review!.ReviewId, () => eligible, (_, _, _) =>
        {
            persisted = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        if (string.Equals(failure, "cancel", StringComparison.Ordinal)) { await execute.Should().ThrowAsync<OperationCanceledException>(); }
        else { (await execute()).Should().NotBe(LocalFileOutcome.Admitted); }
        persisted.Should().BeFalse();
        if (string.Equals(failure, "release", StringComparison.Ordinal))
        {
            var dispose = async () => await action.DisposeAsync();
            await dispose.Should().ThrowAsync<InvalidOperationException>();
        }
        else { await action.DisposeAsync(); }
    }

    [Fact]
    public async Task MissingPersistenceConsumerCannotBeRelabelledAsDurableAdmission()
    {
        var inspector = new Inspector(new Selection([(byte)'a']));
        await using var action = new LocalSessionFileAttach(inspector, new Audit(), TimeProvider.System,
            NullLogger<LocalFilePreview>.Instance);
        var execute = () => action.Execute(Guid.NewGuid(), () => true, null!, TestContext.Current.CancellationToken);
        await execute.Should().ThrowAsync<ArgumentNullException>();
        inspector.Calls.Should().Be(0);
    }

    private sealed class Picker : IUserFilePicker
    {
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(@"C:\Team\source.md");
    }

    private sealed class Inspector(Selection selection) : ILocalFileInspector
    {
        internal int Calls { get; private set; }
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<ILocalFileSelection>(selection);
        }
    }

    private sealed class Selection(byte[] bytes) : ILocalFileSelection
    {
        internal Action? BeforeRead { get; init; }
        internal bool ReleaseFailure { get; init; }
        internal int Disposals { get; private set; }
        public LocalFileMetadata Metadata { get; } = new(@"C:\Team\source.md", "native", bytes.Length, DateTimeOffset.UnixEpoch);
        public Task<byte[]> ReadAsync(CancellationToken cancellationToken)
        {
            BeforeRead?.Invoke();
            return Task.FromResult(bytes);
        }
        public void Dispose()
        {
            Disposals++;
            if (ReleaseFailure) { throw new IOException("release"); }
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
