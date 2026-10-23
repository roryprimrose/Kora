using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Artifacts;
using Kora.Windows.Context;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsProfileInstructionReaderTests : IDisposable
{
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        ".net-test-artifacts", "pf", Guid.NewGuid().ToString("N")[..8]));
    private string Roaming => Path.Combine(root, "profile");
    private string Source => Path.Combine(Roaming, "Code", "User", "prompts", "example.instructions.md");
    private WindowsProfileInstructionReader Reader => new(new WindowsLocalFileInspector(new Paths(root)), Roaming);
    private static byte[] Bytes => ArtifactFileDocument.Encode("---\r\nkora-format: 1\r\nname: example\r\ndescription: Test\r\n---\r\nBody\r\n", true);
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public WindowsProfileInstructionReaderTests() => ActivitySource.AddActivityListener(listener);

    [Fact]
    public async Task ActualVerifiedLeasePinsSourceAcrossCaptureReviewAndDisabledAtomicPublicationThenReleasesAllHandles()
    {
        CreateSource();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var reader = Reader;
        var original = Bytes;
        using (var selection = await reader.OpenSelection(Source, TestContext.Current.CancellationToken))
        {
            var captured = await selection.Read(TestContext.Current.CancellationToken);
            captured.Should().Equal(original);
            var snapshot = ProfileInstructionSnapshot.Parse(selection.Id, selection.Metadata, captured);
            var store = new WindowsOwnedInstructionStore(new Paths(root));
            var proposed = snapshot.Document.WithProfileInstructionCopyName("separate", snapshot.Identity);
            var result = await selection.WithCurrent(snapshot, () => true,
                fresh => store.Write("separate", null, proposed, fresh, TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);
            result.Published.Should().BeTrue();
            result.Current!.Document.CopyBytes().Should().Equal(proposed.CopyBytes());
            File.ReadAllBytes(Source).Should().Equal(original);
            ((Action)(() => File.WriteAllBytes(Source, original))).Should().Throw<IOException>();
            ((Action)(() => File.Move(Source, Source + ".moved"))).Should().Throw<IOException>();
        }
        File.WriteAllBytes(Source, original);
        File.Move(Source, Source + ".moved");
    }

    [Theory]
    [InlineData("SKILL.md")]
    [InlineData("example.prompt.md")]
    [InlineData("example.agent.md")]
    [InlineData("example.Instructions.md")]
    public async Task OtherKindsAndNoncanonicalExtensionsAreNotAdmitted(string file)
    {
        CreateSource();
        var operation = () => Reader.OpenSelection(Path.Combine(Path.GetDirectoryName(Source)!, file), TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("foreign-profile")]
    [InlineData("nested")]
    [InlineData("profile-without-prompts")]
    public async Task NonPersonalOrUnboundedLocationsAreRefusedBeforeOpening(string location)
    {
        CreateSource();
        var path = location switch
        {
            "workspace" => Path.Combine(root, "workspace", "example.instructions.md"),
            "foreign-profile" => Path.Combine(root, "foreign", "Code", "User", "prompts", "example.instructions.md"),
            "nested" => Path.Combine(Path.GetDirectoryName(Source)!, "nested", "example.instructions.md"),
            _ => Path.Combine(Roaming, "Code", "User", "profiles", "one", "example.instructions.md"),
        };
        var operation = () => Reader.OpenSelection(path, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("withdrawn")]
    [InlineData("foreign-lease")]
    [InlineData("changed-bytes")]
    [InlineData("cancelled")]
    [InlineData("disposed")]
    public async Task FinalGuardRejectsStaleOrForeignSnapshotsAndNeverCallsPublication(string failure)
    {
        CreateSource();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        using var selection = await Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
        var captured = await selection.Read(TestContext.Current.CancellationToken);
        var snapshot = ProfileInstructionSnapshot.Parse(failure is "foreign-lease" ? Guid.NewGuid() : selection.Id,
            selection.Metadata, captured);
        if (failure is "changed-bytes")
        {
            captured[^3] = (byte)'X';
            snapshot = ProfileInstructionSnapshot.Parse(selection.Id, selection.Metadata, captured);
        }
        using var cancellation = new CancellationTokenSource();
        if (failure is "cancelled") { cancellation.Cancel(); }
        if (failure is "disposed") { selection.Dispose(); }
        var published = false;
        var operation = () => selection.WithCurrent(snapshot, () => failure is not "withdrawn", _ =>
        {
            published = true;
            return Task.FromResult(new InstructionWriteResult(false, false, false, null));
        }, cancellation.Token);
        await operation.Should().ThrowAsync<Exception>();
        published.Should().BeFalse();
        selection.Dispose();
        File.WriteAllBytes(Source, Bytes);
    }

    [Fact]
    public async Task ExistingWritableMappingIsRejectedBeforeSourceCaptureOrPublication()
    {
        CreateSource();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        MemoryMappedViewAccessor view;
        using (var stream = new FileStream(Source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        using (var mapping = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true))
        { view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite); }
        using (view)
        {
            var operation = () => Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
            await operation.Should().ThrowAsync<IOException>();
            File.Exists(Path.Combine(new Paths(root).RoamingRoot, "Instructions", "separate.instructions.md")).Should().BeFalse();
        }
        File.WriteAllBytes(Source, Bytes);
    }

    [Fact]
    public async Task ConsentWithdrawalAfterDestinationStagingIsRejectedByFinalWriterGuard()
    {
        CreateSource();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        using var selection = await Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
        var snapshot = ProfileInstructionSnapshot.Parse(selection.Id, selection.Metadata,
            await selection.Read(TestContext.Current.CancellationToken));
        var consent = true;
        var store = new WindowsOwnedInstructionStore(new Paths(root)) { AfterFinalComparison = () => consent = false };
        var proposed = snapshot.Document.WithProfileInstructionCopyName("separate", snapshot.Identity);
        var operation = () => selection.WithCurrent(snapshot, () => consent,
            fresh => store.Write("separate", null, proposed, fresh, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Path.Combine(new Paths(root).RoamingRoot, "Instructions", "separate.instructions.md")).Should().BeFalse();
        Directory.GetFiles(Path.Combine(new Paths(root).RoamingRoot, "Instructions"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SourceFreshnessReadsCannotExceedExplicitConsentBudget()
    {
        CreateSource();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        using var selection = await Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
        var snapshot = ProfileInstructionSnapshot.Parse(selection.Id, selection.Metadata,
            await selection.Read(TestContext.Current.CancellationToken));
        var operation = () => selection.WithCurrent(snapshot, () => true, fresh =>
        {
            for (var count = 0; count < ProfileInstructionSnapshot.MaximumFreshnessChecks; count++) { fresh().Should().BeTrue(); }
            return Task.FromResult(new InstructionWriteResult(false, false, false, null));
        }, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidOperationException>().WithMessage("*consent is exhausted*");
    }

    [Theory]
    [InlineData("Code", false)]
    [InlineData("Code - Insiders", true)]
    public async Task ExactRegisteredFormatLocationsIncludeOnlyImmediatePersonalPromptFiles(string editor, bool namedProfile)
    {
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var path = namedProfile
            ? Path.Combine(Roaming, editor, "User", "profiles", "one", "prompts", "example.instructions.md")
            : Path.Combine(Roaming, editor, "User", "prompts", "example.instructions.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WindowsOwnedInstructionStoreTests.WriteOwnedFile(path, Bytes);
        using var selection = await Reader.OpenSelection(path, TestContext.Current.CancellationToken);
        selection.Metadata.CanonicalPath.Should().Be(path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public async Task EmptyOrOversizedInstructionIsRejectedAndItsMetadataLeaseIsReleased(int length)
    {
        CreateSource();
        File.WriteAllBytes(Source, new byte[length]);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var operation = () => Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidDataException>();
        File.WriteAllBytes(Source, Bytes);
    }

    [Fact]
    public async Task MissingSupportedOriginalIsExplicitlyUnavailableWithoutRecreation()
    {
        CreateSource();
        File.Delete(Source);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var operation = () => Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<IOException>();
        File.Exists(Source).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaseAliasAndHardLinkedPersonalOriginalsRemainUnavailable(bool hardLink)
    {
        CreateSource();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var selected = Path.Combine(Path.GetDirectoryName(Source)!, hardLink ? "linked.instructions.md" : "EXAMPLE.instructions.md");
        if (hardLink) { CreateHardLink(selected, Source, nint.Zero).Should().BeTrue(); }
        var operation = () => Reader.OpenSelection(selected, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(Source).Should().Equal(Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrExpiredHostTraceCannotReadSourceAndRetiredLeaseReleasesHandles(bool expired)
    {
        CreateSource();
        IProfileInstructionSelection? selected = null;
        try
        {
            if (expired)
            {
                using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
                selected = await Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
            }
            if (selected is null)
            {
                var open = () => Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
                await open.Should().ThrowAsync<InvalidOperationException>().WithMessage("*live host-resolved activity*");
            }
            else
            {
                var read = () => selected.Read(TestContext.Current.CancellationToken);
                await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*live host-resolved activity*");
            }
        }
        finally { selected?.Dispose(); }
        File.WriteAllBytes(Source, Bytes);
    }

    private void CreateSource()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        WindowsOwnedInstructionStoreTests.WriteOwnedFile(Source, Bytes);
    }

    [Fact]
    public async Task PersonalInstructionUnderHiddenKnownRoamingAncestorCanBeCapturedWithoutBroadeningOrdinaryFileReads()
    {
        var appData = Path.Combine(root, "profile", "AppData");
        var roaming = Path.Combine(appData, "Roaming");
        var source = Path.Combine(roaming, "Code", "User", "prompts", "example.instructions.md");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        WindowsOwnedInstructionStoreTests.WriteOwnedFile(source, Bytes);
        File.SetAttributes(appData, File.GetAttributes(appData) | FileAttributes.Hidden);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var inspector = new WindowsLocalFileInspector(new Paths(root));
        var ordinary = () => inspector.InspectAsync(source, TestContext.Current.CancellationToken);
        await ordinary.Should().ThrowAsync<InvalidDataException>();
        var reader = new WindowsProfileInstructionReader(inspector, roaming);
        using var selection = await reader.OpenSelection(source, TestContext.Current.CancellationToken);
        (await selection.Read(TestContext.Current.CancellationToken)).Should().Equal(Bytes);
        var snapshot = ProfileInstructionSnapshot.Parse(selection.Id, selection.Metadata, Bytes);
        var result = await selection.WithCurrent(snapshot, () => true, current =>
        {
            current().Should().BeTrue();
            return Task.FromResult(new InstructionWriteResult(false, false, false, null));
        }, TestContext.Current.CancellationToken);
        result.Published.Should().BeFalse();
    }

    [Theory]
    [InlineData("editor", FileAttributes.Hidden)]
    [InlineData("file", FileAttributes.Hidden)]
    [InlineData("roaming", FileAttributes.System)]
    public async Task ProfileAncestorExceptionNeverAdmitsHiddenDescendantsOrSystemDirectories(string target, FileAttributes attributes)
    {
        CreateSource();
        var path = target switch
        {
            "editor" => Path.Combine(Roaming, "Code"),
            "file" => Source,
            _ => Roaming,
        };
        var before = File.GetAttributes(path);
        File.SetAttributes(path, before | attributes);
        try
        {
            using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
            var operation = () => Reader.OpenSelection(Source, TestContext.Current.CancellationToken);
            await operation.Should().ThrowAsync<InvalidDataException>();
        }
        finally { File.SetAttributes(path, before); }
        File.WriteAllBytes(Source, Bytes);
    }

    [Fact]
    public async Task NewHiddenDescendantAfterReadConsentFailsFinalRevalidationAndReleasesLease()
    {
        CreateSource();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var directory = Path.GetDirectoryName(Source)!;
        var before = File.GetAttributes(directory);
        using (var selection = await Reader.OpenSelection(Source, TestContext.Current.CancellationToken))
        {
            var snapshot = ProfileInstructionSnapshot.Parse(selection.Id, selection.Metadata,
                await selection.Read(TestContext.Current.CancellationToken));
            File.SetAttributes(directory, before | FileAttributes.Hidden);
            try
            {
                var operation = () => selection.WithCurrent(snapshot, () => true,
                    _ => throw new InvalidOperationException("Publication must not run"), TestContext.Current.CancellationToken);
                await operation.Should().ThrowAsync<InvalidDataException>();
            }
            finally { File.SetAttributes(directory, before); }
        }
        File.WriteAllBytes(Source, Bytes);
    }

    public void Dispose()
    {
        listener.Dispose();
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => Path.Combine(root, "host-local");
        public string RoamingRoot => Path.Combine(root, "host-roaming");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string target, nint security);
}