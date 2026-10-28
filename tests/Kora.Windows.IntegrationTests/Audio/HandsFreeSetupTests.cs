using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Testing;
using Kora.Windows.Audio;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Audio;

[Collection("Host tracing")]
public sealed class HandsFreeSetupTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public HandsFreeSetupTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Production_offer_pins_real_shipped_native_and_complete_data_but_grants_no_capture_qualification()
    {
        var component = HandsFreeSetupCatalogue.Component;
        HandsFreeSetupCatalogue.Production.Components.Should().Equal(component);
        component.Assets.Should().HaveCount(13);
        component.DownloadBytes.Should().Be(12222276);
        component.Assets.Should().OnlyContain(asset => asset.Source.Host == "raw.githubusercontent.com");
        component.Assets.Select(asset => asset.Name).Should().NotContain(name => name.EndsWith(".dll", StringComparison.Ordinal));
        var bytes = await File.ReadAllBytesAsync(HandsFreeSetupCatalogue.NativePath, TestContext.Current.CancellationToken);
        bytes.LongLength.Should().Be(HandsFreeSetupCatalogue.NativeBytes);
        Convert.ToHexString(SHA256.HashData(bytes)).Should().Be(HandsFreeSetupCatalogue.NativeSha256);
        await using var runtime = new HandsFreeSetupRuntime(new Dependencies.InMemorySpeechComponentFiles());
        var profile = runtime.Profiles.Should().ContainSingle().Subject;
        profile.CaptureQualified.Should().BeFalse();
        profile.Names.Should().BeEmpty();
        profile.ConfigurationFiles.Keys.Should().BeEquivalentTo(["mdef", "mean", "var", "tmat", "sendump", "fdict", "featparams"]);
        var stage = new WindowsHandsFreeAudioStage(runtime);
        stage.IsAvailable.Should().BeFalse();
        await FluentActions.Awaiting(() => stage.OpenAsync(Binding(), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        runtime.Observe(component).Detail.Should().Contain("Unavailable");
    }

    [Fact]
    public async Task Review_confirm_prepare_native_binding_reconstruct_verify_and_remove_use_one_real_store()
    {
        using var fixture = new Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        var missing = await setup.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken);
        missing.Storage!.Installed.Should().BeFalse();
        fixture.Acquisition.Calls.Should().Be(0);
        var plan = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare, () => true, TestContext.Current.CancellationToken);
        plan.Disclosure.Should().Contain("private Kora").And.Contain("No runtime selection");
        var prepared = await setup.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken);
        prepared.Storage!.Verified.Should().BeTrue();
        prepared.Runtime.Functional.Should().Be(SpeechComponentFunctionalEvidence.Verified);
        prepared.Runtime.Detail.Should().Contain("Prepared").And.Contain("Unavailable").And.Contain("No native library or model was executed");
        fixture.Acquisition.Calls.Should().Be(2);
        new WindowsHandsFreeAudioStage(fixture.Runtime).IsAvailable.Should().BeFalse();

        await using var restarted = new HandsFreeSetupRuntime(fixture.Component, fixture.Profile, compatible: true);
        var reconstructed = fixture.Setup(restarted);
        var readback = await reconstructed.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken);
        readback.Storage.Should().Be(prepared.Storage);
        readback.Runtime.Functional.Should().Be(SpeechComponentFunctionalEvidence.NotChecked);
        var verify = await reconstructed.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Verify, () => true, TestContext.Current.CancellationToken);
        (await reconstructed.ApplyAsync(verify, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Storage!.Verified.Should().BeTrue();
        var personal = Path.Combine(fixture.Files.Root, "personal-profile.data");
        await File.WriteAllTextAsync(personal, "preserve", TestContext.Current.CancellationToken);
        var removal = await reconstructed.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Remove, () => true, TestContext.Current.CancellationToken);
        (await reconstructed.ApplyAsync(removal, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Storage!.Ownership.Should().Be(SpeechComponentOwnership.Missing);
        File.Exists(personal).Should().BeTrue();
        File.Exists(fixture.Native).Should().BeTrue();
        fixture.Acquisition.Calls.Should().Be(2);
        fixture.Audit.Events.Count(entry => entry.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded).Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Denied_or_revoked_review_cannot_publish_or_enable_capture(bool revoke)
    {
        using var fixture = new Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        var admitted = true;
        var plan = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare, () => admitted, TestContext.Current.CancellationToken);
        if (revoke) { fixture.Acquisition.AfterTransfer = () => admitted = false; }
        await FluentActions.Awaiting(() => setup.ApplyAsync(plan, revoke, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        Directory.Exists(fixture.FinalPath).Should().BeFalse();
        new WindowsHandsFreeAudioStage(fixture.Runtime).IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_and_runtime_retirement_join_ignoring_acquisition_before_releasing_ownership()
    {
        using var fixture = new Fixture();
        using var host = Begin();
        using var cancellation = new CancellationTokenSource();
        fixture.Acquisition.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var setup = fixture.Setup();
        var plan = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare, () => true, TestContext.Current.CancellationToken);
        var prepare = setup.ApplyAsync(plan, true, fixture.Progress, cancellation.Token);
        await fixture.Acquisition.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        prepare.IsCompleted.Should().BeFalse();
        (await fixture.Runtime.TryAcquireExclusiveAsync(fixture.Component, TestContext.Current.CancellationToken))
            .State.Should().Be(SpeechComponentMutationState.Busy);
        var retirement = fixture.Runtime.DisposeAsync().AsTask();
        retirement.IsCompleted.Should().BeFalse();
        fixture.Acquisition.Hold.SetResult();
        await FluentActions.Awaiting(() => prepare).Should().ThrowAsync<OperationCanceledException>();
        await retirement;
        Directory.Exists(fixture.FinalPath).Should().BeFalse();
        Directory.GetDirectories(fixture.Files.Root).Should().BeEmpty();
        await FluentActions.Awaiting(async () => await fixture.Runtime.TryAcquireExclusiveAsync(fixture.Component, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Unknown_saved_manifest_and_replaced_bytes_are_not_ready_or_silently_reinstalled()
    {
        using var fixture = new Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        var plan = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare, () => true, TestContext.Current.CancellationToken);
        await setup.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken);
        var marker = Path.Combine(fixture.FinalPath, ".kora-component-v1");
        await File.WriteAllTextAsync(marker, "malformed", TestContext.Current.CancellationToken);
        (await setup.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken))
            .Storage!.Ownership.Should().Be(SpeechComponentOwnership.Foreign);
        var retry = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare, () => true, TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => setup.ApplyAsync(retry, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.Acquisition.Calls.Should().Be(2);
        await File.WriteAllTextAsync(marker, fixture.Component.ManifestDigest, Encoding.ASCII, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(fixture.FinalPath, "model.data"), [4, 3, 2, 1], TestContext.Current.CancellationToken);
        (await setup.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken)).Storage!.Verified.Should().BeFalse();
        var verify = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Verify, () => true, TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => setup.ApplyAsync(verify, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Exact_source_and_platform_are_required_and_capture_cannot_enter_while_setup_owns_assets()
    {
        using var fixture = new Fixture(qualifiedSyntheticProfile: true);
        var foreign = SpeechComponentFixture.Definition(ram: 33);
        FluentActions.Invoking(() => fixture.Runtime.Observe(foreign)).Should().Throw<InvalidOperationException>();
        await using var unavailable = new HandsFreeSetupRuntime(fixture.Component, fixture.Profile, compatible: false);
        unavailable.Observe(fixture.Component).Compatibility.Should().Be(SpeechComponentCompatibility.Incompatible);
        new WindowsHandsFreeAudioStage(unavailable).IsAvailable.Should().BeFalse();
        (await unavailable.TryAcquireExclusiveAsync(fixture.Component, TestContext.Current.CancellationToken)).State.Should().Be(SpeechComponentMutationState.Unavailable);
        var owner = (await fixture.Runtime.TryAcquireExclusiveAsync(fixture.Component, TestContext.Current.CancellationToken)).RequireLease();
        var stage = new WindowsHandsFreeAudioStage(fixture.Runtime);
        stage.IsAvailable.Should().BeTrue();
        await FluentActions.Awaiting(() => stage.OpenAsync(Binding(), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*owned by setup*");
        await owner.DisposeAsync();
        await owner.DisposeAsync();
        await using var next = (await fixture.Runtime.TryAcquireExclusiveAsync(fixture.Component, TestContext.Current.CancellationToken)).RequireLease();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_or_extra_native_code_never_produces_a_prepared_receipt(bool extraLibrary)
    {
        using var fixture = new Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        var prepare = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare, () => true, TestContext.Current.CancellationToken);
        await setup.ApplyAsync(prepare, true, fixture.Progress, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(extraLibrary ? Path.Combine(Path.GetDirectoryName(fixture.Native)!, "unadmitted.dll") : fixture.Native,
            [4, 3, 2, 1], TestContext.Current.CancellationToken);
        var verify = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Verify, () => true, TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => setup.ApplyAsync(verify, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.Audit.Events.Last().Outcome.Should().Be(Kora.Core.Auditing.SecurityAuditOutcome.Failed);
        await using var next = (await fixture.Runtime.TryAcquireExclusiveAsync(fixture.Component, TestContext.Current.CancellationToken)).RequireLease();
    }

    [Fact]
    public async Task Exact_native_and_model_handles_stay_pinned_until_exclusive_verification_owner_retires()
    {
        using var fixture = new Fixture();
        using var host = Begin();
        var setup = fixture.Setup();
        var prepare = await setup.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare, () => true, TestContext.Current.CancellationToken);
        await setup.ApplyAsync(prepare, true, fixture.Progress, TestContext.Current.CancellationToken);
        var owner = (await fixture.Runtime.TryAcquireExclusiveAsync(fixture.Component, TestContext.Current.CancellationToken)).RequireLease();
        await fixture.Runtime.VerifyAsync(fixture.Component, TestContext.Current.CancellationToken);
        FluentActions.Invoking(() => File.Open(fixture.Native, FileMode.Open, FileAccess.Write, FileShare.None).Dispose())
            .Should().Throw<IOException>();
        var retirement = fixture.Runtime.DisposeAsync().AsTask();
        retirement.IsCompleted.Should().BeFalse();
        await owner.DisposeAsync();
        await retirement;
        using var writable = File.Open(fixture.Native, FileMode.Open, FileAccess.Write, FileShare.None);
    }

    private static HandsFreeAudioBinding Binding() => new(1, new("synthetic", "Synthetic"), "Nova",
        new(1, SpeechInputSelection.Default)) { CanCapture = () => true };
    private static HostActivity Begin() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);

    internal sealed class Fixture : IDisposable
    {
        internal SpeechComponentDefinition Component { get; } = SpeechComponentFixture.Definition();
        internal DiskFiles Files { get; }
        internal Acquisition Acquisition { get; } = new();
        internal HandsFreeNativeProfile Profile { get; }
        internal HandsFreeSetupRuntime Runtime { get; }
        internal SpeechComponentSetupFixture.Audit Audit { get; } = new();
        internal IProgress<string> Progress { get; } = new SpeechComponentSetupFixture.SynchronousProgress(_ => { });
        internal string FinalPath => Path.Combine(Files.Root, Component.Identity.StorageName);
        internal string Native { get; }
        private readonly string root;
        internal Fixture(bool qualifiedSyntheticProfile = false)
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kora.slnx"))) { repository = repository.Parent; }
            root = Path.Combine(repository!.FullName, ".scratch", "hands-free-setup-tests", Guid.NewGuid().ToString("N"));
            Files = new(Path.Combine(root, "assets"));
            Native = Path.Combine(root, "native", "fixture.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(Native)!);
            File.WriteAllBytes(Native, SpeechComponentFixture.Bytes);
            var assets = Component.Assets.Select(asset => new HandsFreeNativeAsset(Path.Combine(FinalPath, asset.Name), asset.SizeBytes, asset.Sha256))
                .Concat([new(Native, 4, Convert.ToHexString(SHA256.HashData(SpeechComponentFixture.Bytes))),
                    new(Path.Combine(FinalPath, ".kora-component-v1"), 64, Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(Component.ManifestDigest))))]);
            Profile = new(Native, FinalPath, Path.Combine(FinalPath, "model.data"), Path.Combine(FinalPath, "tokens.data"),
                assets, qualifiedSyntheticProfile ? ["Nova"] : [], qualifiedSyntheticProfile ? "synthetic-only" : null);
            Runtime = new(Component, Profile, compatible: true);
        }
        internal SpeechComponentSetupService Setup(HandsFreeSetupRuntime? runtime = null) =>
            new(new([Component]), new WindowsSpeechComponentStore(Files, Acquisition), runtime ?? Runtime, Audit,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SpeechComponentSetupService>.Instance, TimeProvider.System);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }

    internal sealed class DiskFiles(string root) : ISpeechComponentFiles
    {
        public string Root => root;
        public bool RootExists() => Directory.Exists(Root);
        public void EnsureRoot() => Directory.CreateDirectory(Root);
        public Task<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable>(new Lease());
        public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        public IReadOnlyList<string> Names(string path) => Directory.GetFileSystemEntries(path).Select(entry => Path.GetFileName(entry)).ToArray();
        public Stream Read(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        public Stream Create(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
        public void MoveFile(string source, string destination) => File.Move(source, destination);
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path) => Directory.Delete(path);
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    internal sealed class Acquisition : ISpeechComponentAcquisition
    {
        internal int Calls { get; private set; }
        internal Action? AfterTransfer { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? Hold { get; set; }
        public async Task DownloadAsync(SpeechComponentAsset asset, Stream destination, CancellationToken cancellationToken)
        {
            Calls++;
            Entered.TrySetResult();
            if (Hold is { } hold) { await hold.Task; }
            await destination.WriteAsync(SpeechComponentFixture.Bytes, CancellationToken.None);
            AfterTransfer?.Invoke();
        }
    }
}
