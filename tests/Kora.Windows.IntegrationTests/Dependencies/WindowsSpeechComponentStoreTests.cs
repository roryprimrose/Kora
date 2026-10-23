using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Setup;
using Kora.Testing;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class WindowsSpeechComponentStoreTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public WindowsSpeechComponentStoreTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task ExactDataIsStagedVerifiedPublishedReusedAndRemovedWithoutTouchingPersonalOrSharedData()
    {
        var fixture = new Fixture();
        using var host = Begin();
        var missing = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        missing.Ownership.Should().Be(SpeechComponentOwnership.Missing);
        fixture.Files.RootPresent.Should().BeFalse();
        fixture.Download.Calls.Should().Be(0);
        await fixture.Prepare(missing);
        var installed = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        installed.Should().BeEquivalentTo(new SpeechComponentSnapshot(true, true, SpeechComponentOwnership.Managed, installed.Stamp));
        fixture.Download.Calls.Should().Be(2);
        fixture.Files.Moves.Should().Be(1);
        fixture.Files.Files.Should().HaveCount(3);
        fixture.Files.Directories.Should().NotContain(path => path.EndsWith(".staging", StringComparison.Ordinal));
        await fixture.Prepare(installed);
        fixture.Download.Calls.Should().Be(2);
        var personal = Path.Combine(fixture.Files.Root, "personal-profile.data");
        fixture.Files.Files[personal] = [9, 9, 9];
        var shared = Path.Combine(fixture.Files.Root, "another-component.data");
        fixture.Files.Files[shared] = [8, 8, 8];
        await fixture.Store.RemoveAsync(fixture.Component, installed, () => true, CancellationToken.None);
        (await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None)).Ownership.Should().Be(SpeechComponentOwnership.Missing);
        fixture.Files.Files.Keys.Should().BeEquivalentTo([personal, shared]);
        fixture.Files.Files[personal].Should().Equal(9, 9, 9);
        fixture.Files.Files[shared].Should().Equal(8, 8, 8);
        fixture.Files.Releases.Should().Be(fixture.Files.Leases);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("network")]
    [InlineData("cancel")]
    [InlineData("revoked")]
    [InlineData("publish-failure")]
    public async Task FailedTransferOrAdmissionRollsBackOnlyOwnedStagingAndNeverPublishes(string failure)
    {
        var fixture = new Fixture();
        using var host = Begin();
        var missing = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        var admitted = true;
        if (failure is "hash") { fixture.Download.Bytes = [4, 3, 2, 1]; }
        if (failure is "short") { fixture.Download.Bytes = [1]; }
        if (failure is "long") { fixture.Download.Bytes = [1, 2, 3, 4, 5]; }
        if (failure is "network") { fixture.Download.Failure = new HttpRequestException("Fixture-only transfer error."); }
        if (failure is "cancel") { fixture.Download.Failure = new OperationCanceledException(); }
        if (failure is "revoked") { fixture.Download.AfterDownload = () => admitted = false; }
        if (failure is "publish-failure") { fixture.Files.ThrowBeforeMove = true; }
        await FluentActions.Awaiting(() => fixture.Store.PrepareAsync(fixture.Component, missing, () => admitted,
            fixture.Progress, CancellationToken.None)).Should().ThrowAsync<Exception>();
        fixture.Files.Files.Should().BeEmpty();
        fixture.Files.Directories.Should().Equal(fixture.Files.Root);
        fixture.Files.Moves.Should().Be(0);
        fixture.Files.Releases.Should().Be(fixture.Files.Leases);
    }

    [Fact]
    public async Task ConsentRevocationFromProgressCannotRaceAtomicPublication()
    {
        var fixture = new Fixture();
        using var host = Begin();
        var missing = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        var admitted = true;
        var progress = new SpeechComponentSetupFixture.SynchronousProgress(message =>
        {
            if (message.StartsWith("Publishing", StringComparison.Ordinal)) { admitted = false; }
        });
        await FluentActions.Awaiting(() => fixture.Store.PrepareAsync(fixture.Component, missing, () => admitted,
            progress, CancellationToken.None)).Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Files.Files.Should().BeEmpty();
        fixture.Files.Moves.Should().Be(0);
    }

    [Fact]
    public async Task AmbiguousPublicationReconcilesExactIdentityBeforeExplicitRetryWithoutRedownloadOrOverwrite()
    {
        var fixture = new Fixture();
        using var host = Begin();
        var missing = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        fixture.Files.ThrowAfterMove = true;
        await FluentActions.Awaiting(() => fixture.Prepare(missing)).Should().ThrowAsync<IOException>();
        var actual = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        actual.Verified.Should().BeTrue();
        fixture.Files.ThrowAfterMove = false;
        await FluentActions.Awaiting(() => fixture.Prepare(missing)).Should().ThrowAsync<InvalidDataException>();
        await fixture.Prepare(actual);
        fixture.Download.Calls.Should().Be(2);
        fixture.Files.Moves.Should().Be(1);
    }

    [Theory]
    [InlineData("unknown-file")]
    [InlineData("wrong-manifest")]
    [InlineData("no-manifest")]
    [InlineData("definition-changed")]
    public async Task ForeignIdentityAndAdditionalFilesCannotBeOverwrittenOrRemoved(string scenario)
    {
        var fixture = new Fixture();
        using var host = Begin();
        await fixture.Prepare(await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None));
        var definition = fixture.Component;
        if (scenario is "unknown-file") { fixture.Files.Files[Path.Combine(fixture.FinalPath, "personal.data")] = [9]; }
        if (scenario is "wrong-manifest") { fixture.Files.Files[fixture.Marker] = Encoding.ASCII.GetBytes(new string('A', 64)); }
        if (scenario is "no-manifest") { fixture.Files.Files.Remove(fixture.Marker); }
        if (scenario is "definition-changed") { definition = SpeechComponentFixture.Definition(ram: 33); }
        var state = await fixture.Store.InspectAsync(definition, CancellationToken.None);
        state.Ownership.Should().Be(SpeechComponentOwnership.Foreign);
        var before = fixture.Files.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        await FluentActions.Awaiting(() => fixture.Store.PrepareAsync(definition, state, () => true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Awaiting(() => fixture.Store.RemoveAsync(definition, state, () => true, CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Files.Files.Should().BeEquivalentTo(before);
        fixture.Download.Calls.Should().Be(2);
    }

    [Fact]
    public async Task CorruptOwnedContentRequiresExplicitRemovalRatherThanBlindReplacement()
    {
        var fixture = new Fixture();
        using var host = Begin();
        await fixture.Prepare(await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None));
        var previous = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        fixture.Files.Files[Path.Combine(fixture.FinalPath, "model.data")] = [4, 3, 2, 1];
        var changed = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        changed.Verified.Should().BeFalse();
        changed.Stamp.Should().NotBe(previous.Stamp);
        await FluentActions.Awaiting(() => fixture.Store.RemoveAsync(fixture.Component, previous, () => true, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Awaiting(() => fixture.Prepare(changed)).Should().ThrowAsync<InvalidDataException>();
        await fixture.Store.RemoveAsync(fixture.Component, changed, () => true, CancellationToken.None);
        await fixture.Prepare(await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None));
        fixture.Download.Calls.Should().Be(4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialRemovalRetainsExactRecoveryIdentityAndFreshConsentCanCompleteIt(bool finalDirectoryFailure)
    {
        var fixture = new Fixture();
        using var host = Begin();
        await fixture.Prepare(await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None));
        var installed = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        var admitted = true;
        if (finalDirectoryFailure) { fixture.Files.FailFinalDirectoryDelete = true; }
        else { fixture.Files.AfterDelete = _ => admitted = false; }
        await FluentActions.Awaiting(() => fixture.Store.RemoveAsync(fixture.Component, installed, () => admitted, CancellationToken.None))
            .Should().ThrowAsync<Exception>();
        var pending = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        pending.Ownership.Should().Be(SpeechComponentOwnership.RemovalPending);
        pending.Installed.Should().BeFalse();
        await FluentActions.Awaiting(() => fixture.Prepare(pending)).Should().ThrowAsync<InvalidDataException>();
        fixture.Files.FailFinalDirectoryDelete = false;
        fixture.Files.AfterDelete = null;
        await fixture.Store.RemoveAsync(fixture.Component, pending, () => true, CancellationToken.None);
        (await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None)).Ownership.Should().Be(SpeechComponentOwnership.Missing);
        fixture.Files.Files.Should().BeEmpty();
        fixture.Download.Calls.Should().Be(2);
    }

    [Fact]
    public async Task UnsafeRootFailsClosedBeforeAnyAcquisitionOrMutation()
    {
        var fixture = new Fixture();
        using var host = Begin();
        var missing = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        fixture.Files.UnsafeRoot = true;
        await FluentActions.Awaiting(() => fixture.Store.InspectAsync(fixture.Component, CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => fixture.Prepare(missing)).Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Download.Calls.Should().Be(0);
        fixture.Files.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingRemovalIsIdempotentAndUnqualifiedDataCannotBePrepared()
    {
        var fixture = new Fixture();
        using var host = Begin();
        var missing = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        await fixture.Store.RemoveAsync(fixture.Component, missing, () => true, CancellationToken.None);
        var unqualified = SpeechComponentFixture.Definition(null);
        await FluentActions.Awaiting(() => fixture.Store.PrepareAsync(unqualified, missing, () => true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Files.RootPresent.Should().BeFalse();
    }

    [Fact]
    public async Task InterruptedStageAndUnrelatedRevisionArePreservedDuringFreshPreparation()
    {
        var fixture = new Fixture();
        using var host = Begin();
        fixture.Files.EnsureRoot();
        var abandoned = Path.Combine(fixture.Files.Root, fixture.Component.Identity.StorageName + ".abandoned.staging");
        fixture.Files.Directories.Add(abandoned);
        var unknown = Path.Combine(abandoned, "unknown.data");
        fixture.Files.Files[unknown] = [9];
        var otherRevision = Path.Combine(fixture.Files.Root, "component-fixture-input.v0");
        fixture.Files.Directories.Add(otherRevision);
        var previous = Path.Combine(otherRevision, "model.data");
        fixture.Files.Files[previous] = [8];
        await fixture.Prepare(await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None));
        fixture.Files.Files[unknown].Should().Equal(9);
        fixture.Files.Files[previous].Should().Equal(8);
        (await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None)).Verified.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrOversizeManagedDataIsNotVerified(bool oversize)
    {
        var fixture = new Fixture();
        using var host = Begin();
        await fixture.Prepare(await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None));
        var file = Path.Combine(fixture.FinalPath, "model.data");
        if (oversize) { fixture.Files.Files[file] = [1, 2, 3, 4, 5]; }
        else { fixture.Files.Files.Remove(file); }
        var observed = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        observed.Installed.Should().BeTrue();
        observed.Verified.Should().BeFalse();
        observed.Ownership.Should().Be(SpeechComponentOwnership.Managed);
    }

    [Fact]
    public async Task TamperedRecoveryRecordCannotAuthorizeRemovalOrReplacement()
    {
        var fixture = new Fixture();
        using var host = Begin();
        await fixture.Prepare(await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None));
        var installed = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        fixture.Files.FailFinalDirectoryDelete = true;
        await FluentActions.Awaiting(() => fixture.Store.RemoveAsync(fixture.Component, installed, () => true, CancellationToken.None))
            .Should().ThrowAsync<IOException>();
        fixture.Files.Files[fixture.FinalPath + ".removal-record"] = [1, 2, 3];
        var unknown = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        unknown.Ownership.Should().Be(SpeechComponentOwnership.Foreign);
        await FluentActions.Awaiting(() => fixture.Store.RemoveAsync(fixture.Component, unknown, () => true, CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => fixture.Prepare(unknown)).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeMutationDoesNotCreateDataRoot(bool removal)
    {
        var fixture = new Fixture();
        using var host = Begin();
        var missing = await fixture.Store.InspectAsync(fixture.Component, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => removal
            ? fixture.Store.RemoveAsync(fixture.Component, missing, () => true, cancellation.Token)
            : fixture.Store.PrepareAsync(fixture.Component, missing, () => true, fixture.Progress, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        fixture.Files.RootPresent.Should().BeFalse();
        fixture.Download.Calls.Should().Be(0);
    }

    [Fact]
    public async Task InstallerAndSettingsUseSameExactServiceAndUncheckedChoicesNeverAuthorizeSetup()
    {
        var fixture = new Fixture();
        var application = new SpeechComponentSetupFixture();
        var shared = application.Create(fixture.Store);
        IReadOnlyList<SpeechComponentInstallChoice> choices;
        Guid originalSession;
        using (var review = Begin())
        {
            originalSession = review.Request.SessionId.Value;
            choices = await SpeechComponentInstallChoice.InspectAsync(shared, () => true, CancellationToken.None);
        }
        var choice = choices.Should().ContainSingle().Subject;
        choice.Selected.Should().BeFalse();
        choice.Readiness.Storage?.Installed.Should().BeFalse();
        await FluentActions.Awaiting(() => choice.PrepareAsync(fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Download.Calls.Should().Be(0);
        choice.Selected = true;
        await choice.PrepareAsync(fixture.Progress, CancellationToken.None);
        application.AuditLog.Requests.Should().OnlyContain(request => request.SessionId.Value == originalSession);
        application.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        fixture.Download.Calls.Should().Be(2);
        using var settings = Begin();
        var verify = await shared.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Verify, () => true, CancellationToken.None);
        var result = await shared.ApplyAsync(verify, true, fixture.Progress, CancellationToken.None);
        result.Storage?.Verified.Should().BeTrue();
        result.Runtime.Functional.Should().Be(SpeechComponentFunctionalEvidence.Verified);
        fixture.Download.Calls.Should().Be(2);
        await FluentActions.Awaiting(() => choice.PrepareAsync(fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        new OptionalComponents().Any.Should().BeFalse();
        new OptionalComponents { Speech = choices }.Any.Should().BeTrue();
        var mutable = choices.ToList();
        var selection = new OptionalComponents { Speech = mutable };
        var equivalent = new OptionalComponents { Speech = choices };
        selection.Should().Be(equivalent);
        selection.GetHashCode().Should().Be(equivalent.GetHashCode());
        mutable.Clear();
        selection.Speech.Should().ContainSingle();
        selection.Should().NotBe(new OptionalComponents());
    }

    private static HostActivity Begin() =>
        HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);

    private sealed class Fixture
    {
        internal SpeechComponentDefinition Component { get; } = SpeechComponentFixture.Definition();
        internal InMemorySpeechComponentFiles Files { get; } = new();
        internal Acquisition Download { get; } = new();
        internal WindowsSpeechComponentStore Store { get; }
        internal IProgress<string> Progress { get; } = new SpeechComponentSetupFixture.SynchronousProgress(_ => { });
        internal string FinalPath => Path.Combine(Files.Root, Component.Identity.StorageName);
        internal string Marker => Path.Combine(FinalPath, ".kora-component-v1");
        internal Fixture() => Store = new(Files, Download);
        internal Task Prepare(SpeechComponentSnapshot expected) => Store.PrepareAsync(Component, expected, () => true, Progress, CancellationToken.None);
    }

    private sealed class Acquisition : ISpeechComponentAcquisition
    {
        internal byte[] Bytes { get; set; } = SpeechComponentFixture.Bytes;
        internal int Calls { get; private set; }
        internal Exception? Failure { get; set; }
        internal Action? AfterDownload { get; set; }
        public async Task DownloadAsync(SpeechComponentAsset asset, Stream destination, CancellationToken cancellationToken)
        {
            Calls++;
            await destination.WriteAsync(Bytes, cancellationToken);
            if (Failure is { } failure) { throw failure; }
            AfterDownload?.Invoke();
        }
    }
}
