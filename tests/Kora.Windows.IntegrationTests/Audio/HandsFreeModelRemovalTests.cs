using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

[Collection("Host tracing")]
public sealed class HandsFreeModelRemovalTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public HandsFreeModelRemovalTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovalJoinsOnlyOwnedConsumersAndExcludesLateOpenBeforeDeletingRealFixtureFiles(bool lateOpen)
    {
        using var fixture = new HandsFreeSetupTests.Fixture(qualifiedSyntheticProfile: true);
        using var host = Begin();
        var setup = fixture.Setup();
        await Prepare(fixture, setup);
        new WindowsHandsFreeAudioStage(fixture.Runtime).IsAvailable.Should().BeTrue();
        var unowned = Path.Combine(fixture.Files.Root, "user-original.data");
        await File.WriteAllBytesAsync(unowned, [9, 8, 7], TestContext.Current.CancellationToken);
        var capture = (await fixture.Runtime.TryAcquireOwnershipAsync(TestContext.Current.CancellationToken))!;
        var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerJoined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var invalidation = capture.Token.Register(() => { if (lateOpen) { retired.TrySetResult(); } });
        if (!lateOpen)
        {
            capture.Attach(async () =>
            {
                retired.SetResult();
                await workerJoined.Task;
                await capture.DisposeAsync();
            }).Should().BeTrue();
        }
        var review = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken);
        review.RemovalDisclosure.Should().Contain("72").And.Contain("model.data: 4 bytes").And.Contain("tokens.data: 4 bytes")
            .And.Contain("Current owned asset consumers: 1").And.Contain("Independent privacy and consent")
            .And.Contain("Unavailable").And.Contain("biometric").And.Contain("Confirm removal separately");
        review.Readiness.Runtime.NativeIdentity.Should().NotBeNullOrEmpty();
        var removal = setup.ApplyAsync(review, true, fixture.Progress, TestContext.Current.CancellationToken);
        await retired.Task.WaitAsync(TestContext.Current.CancellationToken);
        capture.Token.IsCancellationRequested.Should().BeTrue();
        removal.IsCompleted.Should().BeFalse();
        Directory.GetFiles(fixture.FinalPath).Should().HaveCount(3);
        (await fixture.Runtime.TryAcquireOwnershipAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Requested);
        if (lateOpen)
        {
            capture.Attach(() => throw new InvalidOperationException("Late native work must not start.")).Should().BeFalse();
            await capture.DisposeAsync();
        }
        else { workerJoined.SetResult(); }
        var removed = await removal;
        removed.Storage!.Ownership.Should().Be(SpeechComponentOwnership.Missing);
        removed.Storage.ExactBytes.Should().Be(0);
        Directory.Exists(fixture.FinalPath).Should().BeFalse();
        (await File.ReadAllBytesAsync(unowned, TestContext.Current.CancellationToken)).Should().Equal(9, 8, 7);
        File.Exists(fixture.Native).Should().BeTrue();
        fixture.Acquisition.Calls.Should().Be(2);
        new WindowsHandsFreeAudioStage(fixture.Runtime).IsAvailable.Should().BeFalse();
        (await fixture.Runtime.TryAcquireOwnershipAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task NewConsumerAfterReviewInvalidatesConfirmationWithoutRetiringOrDeletingAnything()
    {
        using var fixture = new HandsFreeSetupTests.Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        await Prepare(fixture, setup);
        var review = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken);
        await using var capture = (await fixture.Runtime.TryAcquireOwnershipAsync(TestContext.Current.CancellationToken))!;
        await FluentActions.Awaiting(() => setup.ApplyAsync(review, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        capture.Token.IsCancellationRequested.Should().BeFalse();
        Directory.GetFiles(fixture.FinalPath).Should().HaveCount(3);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("cancel")]
    [InlineData("quiescence-failure")]
    public async Task AuthorityLossOrUnconfirmedJoinCannotDeleteOrClaimReleasedResources(string scenario)
    {
        using var fixture = new HandsFreeSetupTests.Fixture();
        using var host = Begin();
        using var cancellation = new CancellationTokenSource();
        var setup = fixture.Setup();
        await Prepare(fixture, setup);
        var capture = (await fixture.Runtime.TryAcquireOwnershipAsync(TestContext.Current.CancellationToken))!;
        var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.Attach(async () =>
        {
            retired.SetResult();
            await finish.Task;
            if (scenario is "quiescence-failure")
            {
                capture.Fail();
                throw new InvalidOperationException("Fixture model did not confirm physical join.");
            }
            await capture.DisposeAsync();
        }).Should().BeTrue();
        var admitted = true;
        var review = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => admitted, TestContext.Current.CancellationToken);
        var removal = setup.ApplyAsync(review, true, fixture.Progress, cancellation.Token);
        await retired.Task.WaitAsync(TestContext.Current.CancellationToken);
        if (scenario is "revoke") { admitted = false; }
        if (scenario is "cancel") { await cancellation.CancelAsync(); }
        removal.IsCompleted.Should().BeFalse();
        Directory.GetFiles(fixture.FinalPath).Should().HaveCount(3);
        finish.SetResult();
        await FluentActions.Awaiting(() => removal).Should().ThrowAsync<Exception>();
        Directory.GetFiles(fixture.FinalPath).Should().HaveCount(3);
        fixture.Audit.Events.Last().Outcome.Should().NotBe(SecurityAuditOutcome.Succeeded);
        fixture.Acquisition.Calls.Should().Be(2);
        if (scenario is "quiescence-failure")
        {
            (await fixture.Runtime.TryAcquireOwnershipAsync(TestContext.Current.CancellationToken)).Should().BeNull();
            await FluentActions.Awaiting(() => setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
                () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task SeparateExplicitReacquisitionDoesNotClearTheRemovedProfilesCaptureHold()
    {
        using var fixture = new HandsFreeSetupTests.Fixture(qualifiedSyntheticProfile: true);
        using var host = Begin();
        var setup = fixture.Setup();
        await Prepare(fixture, setup);
        var review = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken);
        await setup.ApplyAsync(review, true, fixture.Progress, TestContext.Current.CancellationToken);
        fixture.Acquisition.Calls.Should().Be(2);
        await Prepare(fixture, setup);
        fixture.Acquisition.Calls.Should().Be(4);
        new WindowsHandsFreeAudioStage(fixture.Runtime).IsAvailable.Should().BeFalse();
        (await fixture.Runtime.TryAcquireOwnershipAsync(TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedFileOrDirectoryWithIdenticalBytesIsNotTheReviewedObject(bool directory)
    {
        using var fixture = new HandsFreeSetupTests.Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        await Prepare(fixture, setup);
        var review = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken);
        if (directory)
        {
            var original = fixture.FinalPath + ".original";
            Directory.Move(fixture.FinalPath, original);
            Directory.CreateDirectory(fixture.FinalPath);
            foreach (var file in Directory.GetFiles(original))
            { File.Copy(file, Path.Combine(fixture.FinalPath, Path.GetFileName(file))); }
        }
        else
        {
            var file = Path.Combine(fixture.FinalPath, "model.data");
            File.Move(file, file + ".original");
            File.Copy(file + ".original", file);
            File.Move(file + ".original", Path.Combine(fixture.Files.Root, "original.data"));
        }
        await FluentActions.Awaiting(() => setup.ApplyAsync(review, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        Directory.GetFiles(fixture.FinalPath).Should().HaveCount(3);
        fixture.Acquisition.Calls.Should().Be(2);
    }

    [Fact]
    public async Task PartialIoFailureReportsActualFiniteRemainderAndNeedsNewIndependentConfirmation()
    {
        using var fixture = new HandsFreeSetupTests.Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        await Prepare(fixture, setup);
        var review = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken);
        fixture.Files.AfterVerifiedDelete = _ => throw new IOException("Fixture failure after one real file deletion.");
        await FluentActions.Awaiting(() => setup.ApplyAsync(review, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<IOException>();
        var remainder = review.RemovalRecovery!.Storage!;
        remainder.Ownership.Should().Be(SpeechComponentOwnership.RemovalPending);
        remainder.Installed.Should().BeFalse();
        remainder.ExactBytes.Should().Be(132);
        remainder.Inventory.Should().Contain("model.data: absent").And.Contain("tokens.data: 4 bytes")
            .And.Contain("Removal recovery record: 64 bytes");
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        await FluentActions.Awaiting(() => setup.ApplyAsync(review, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Files.AfterVerifiedDelete = null;
        var recoveryReview = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken);
        var recovered = await setup.ApplyAsync(recoveryReview, true, fixture.Progress, TestContext.Current.CancellationToken);
        recovered.Storage!.Ownership.Should().Be(SpeechComponentOwnership.Missing);
        fixture.Acquisition.Calls.Should().Be(2);
        File.Exists(fixture.Native).Should().BeTrue();
    }

    [Fact]
    public async Task NonCanonicalDirectoryAliasCannotProduceADeletionConfirmation()
    {
        using var fixture = new HandsFreeSetupTests.Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        await Prepare(fixture, setup);
        var alias = new HandsFreeSetupTests.DiskFiles(fixture.Files.Root + Path.DirectorySeparatorChar + ".");
        var alternate = new SpeechComponentSetupService(new([fixture.Component]),
            new Kora.Windows.Dependencies.WindowsSpeechComponentStore(alias, fixture.Acquisition),
            fixture.Runtime, fixture.Audit,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SpeechComponentSetupService>.Instance, TimeProvider.System);
        await FluentActions.Awaiting(() => alternate.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        Directory.GetFiles(fixture.FinalPath).Should().HaveCount(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryRecordReplacedDuringRemovalIsPreservedUntilFreshExactReview(bool recovering)
    {
        using var fixture = new HandsFreeSetupTests.Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        await Prepare(fixture, setup);
        if (recovering)
        {
            var initial = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
                () => true, TestContext.Current.CancellationToken);
            fixture.Files.AfterVerifiedDelete = _ => throw new IOException("Fixture interruption after one deletion.");
            await FluentActions.Awaiting(() => setup.ApplyAsync(initial, true, fixture.Progress, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<IOException>();
        }
        var review = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken);
        var record = fixture.FinalPath + ".removal-record";
        var original = Path.Combine(fixture.Files.Root, "original-removal-record");
        string? originalIdentity = null;
        string? replacementIdentity = null;
        fixture.Files.AfterVerifiedDelete = path =>
        {
            if (!string.Equals(Path.GetFileName(path), "tokens.data", StringComparison.Ordinal)) { return; }
            originalIdentity = fixture.Files.Identity(record);
            File.Move(record, original);
            File.Copy(original, record);
            replacementIdentity = fixture.Files.Identity(record);
        };
        await FluentActions.Awaiting(() => setup.ApplyAsync(review, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*object changed*");
        originalIdentity.Should().NotBeNullOrEmpty();
        replacementIdentity.Should().NotBeNullOrEmpty().And.NotBe(originalIdentity);
        fixture.Files.Identity(original).Should().Be(originalIdentity);
        fixture.Files.Identity(record).Should().Be(replacementIdentity);
        (await File.ReadAllTextAsync(original, TestContext.Current.CancellationToken)).Should().Be(fixture.Component.ManifestDigest);
        (await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken)).Should().Be(fixture.Component.ManifestDigest);
        Directory.Exists(fixture.FinalPath).Should().BeFalse();
        Directory.Exists(fixture.FinalPath + ".removing").Should().BeFalse();
        review.RemovalRecovery!.Storage!.Ownership.Should().Be(SpeechComponentOwnership.RemovalPending);
        review.RemovalRecovery.Storage.ExactBytes.Should().Be(64);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        (await fixture.Runtime.TryAcquireOwnershipAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        fixture.Acquisition.Calls.Should().Be(2);
        File.Exists(fixture.Native).Should().BeTrue();

        fixture.Files.AfterVerifiedDelete = null;
        var fresh = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove,
            () => true, TestContext.Current.CancellationToken);
        var removed = await setup.ApplyAsync(fresh, true, fixture.Progress, TestContext.Current.CancellationToken);
        removed.Storage!.Ownership.Should().Be(SpeechComponentOwnership.Missing);
        File.Exists(record).Should().BeFalse();
        fixture.Files.Identity(original).Should().Be(originalIdentity);
        fixture.Acquisition.Calls.Should().Be(2);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
    }

    private static async Task Prepare(HandsFreeSetupTests.Fixture fixture, SpeechComponentSetupService setup)
    {
        var plan = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare,
            () => true, TestContext.Current.CancellationToken);
        await setup.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken);
    }

    private static HostActivity Begin() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);
}