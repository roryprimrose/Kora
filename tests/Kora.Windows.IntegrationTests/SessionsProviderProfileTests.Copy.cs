using System.Text.Json;
using System.Xml.Linq;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsProviderProfileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCopyDisclosesBothExactSessionsAndCompleteSparseValuesBeforeDestinationOnlySave(bool inherit)
    {
        await using var f = await NativeCopyFixture.Create(inherit);
        var sourceBefore = await f.Storage.Store.ReadProviderProfile(f.Source, f.Storage.Token);
        var destinationBefore = await f.Storage.Store.ReadProviderProfile(f.Destination, f.Storage.Token);
        f.Viewer.ProviderCopySource = f.Viewer.ProviderCopySources.Single(entry => entry.Authority.SessionId == f.Source);
        f.Viewer.CanReviewProviderCopy.Should().BeTrue();
        await f.Viewer.ReviewProviderCopy();
        f.Viewer.CanConfirmProviders.Should().BeTrue();
        using var review = JsonDocument.Parse(f.Viewer.ProviderStatus);
        review.RootElement.GetProperty("source").GetProperty("Session").GetProperty("SessionId").GetProperty("Value")
            .GetGuid().Should().Be(f.Source.Value);
        review.RootElement.GetProperty("destination").GetProperty("Session").GetProperty("SessionId").GetProperty("Value")
            .GetGuid().Should().Be(f.Destination.Value);
        review.RootElement.GetProperty("previous").GetProperty("Service").GetInt32().Should().Be((int)ProviderService.Codex);
        review.RootElement.GetProperty("proposed").GetProperty("Model").ValueKind.Should().Be(JsonValueKind.Null);
        review.RootElement.GetProperty("ResultingEffective").ValueKind.Should().Be(JsonValueKind.Null);
        review.RootElement.GetProperty("sourceStatus").GetString().Should().Contain("Unavailable");
        (await f.Storage.Store.ReadProviderProfile(f.Destination, f.Storage.Token)).Revision.Should().Be(destinationBefore.Revision);
        await f.Viewer.ConfirmProviders();
        var saved = await f.Storage.Store.ReadProviderProfile(f.Destination, f.Storage.Token);
        saved.Desired.Serialize().Should().Be(sourceBefore.Desired.Serialize());
        saved.Revision.Should().Be(destinationBefore.Revision + 1);
        (await f.Storage.Store.ReadProviderProfile(f.Source, f.Storage.Token)).Should().BeEquivalentTo(sourceBefore);
        f.Preferences.Load().Should().BeNull();
        f.Storage.Count("host_questions").Should().Be(0);
        f.Storage.Count("scoped_grants").Should().Be(0);
        f.Viewer.CanConfirmProviders.Should().BeFalse();
    }

    [Theory]
    [InlineData("source")] [InlineData("field")] [InlineData("surface")] [InlineData("private")]
    [InlineData("control")] [InlineData("selection")] [InlineData("close")] [InlineData("reentrant")]
    public async Task NativeCopyRetiresOnSourceDestinationEditPrivacyCloseAndReentrantNotifications(string change)
    {
        await using var f = await NativeCopyFixture.Create(inherit: false);
        var before = await f.Storage.Store.ReadProviderProfile(f.Destination, f.Storage.Token);
        f.Viewer.ProviderCopySource = f.Viewer.ProviderCopySources.Single(entry => entry.Authority.SessionId == f.Source);
        await f.Viewer.ReviewProviderCopy();
        f.Viewer.CanConfirmProviders.Should().BeTrue();
        switch (change)
        {
            case "source": f.Viewer.ProviderCopySource = f.Viewer.ProviderCopySources.Single(entry => entry.Authority.SessionId == f.Destination); break;
            case "field": f.Viewer.ProviderContext = null; break;
            case "surface": f.Viewer.SetProviderSurfaceOpen(false); break;
            case "private": f.Access.CanInspect = false; f.Access.ControlRevision++; break;
            case "control": f.Access.ControlRevision++; break;
            case "selection": await f.Viewer.SelectAsync(f.Viewer.Sessions.Single(entry => entry.Authority.SessionId == f.Source)); break;
            case "close": f.Viewer.Close(); break;
            case "reentrant":
                var retired = false;
                f.Viewer.PropertyChanged += (_, _) =>
                {
                    if (retired) { return; }
                    retired = true;
                    f.Viewer.ClearProviderReview();
                };
                break;
        }
        await f.Viewer.ConfirmProviders();
        (await f.Storage.Store.ReadProviderProfile(f.Destination, f.Storage.Token)).Revision.Should().Be(before.Revision);
    }

    [Fact]
    public async Task SourcePickerRejectsSameDestinationAndReconstructedMetadataRatherThanAcceptingLabels()
    {
        await using var f = await NativeCopyFixture.Create(inherit: false);
        var source = f.Viewer.ProviderCopySources.Single(entry => entry.Authority.SessionId == f.Source);
        f.Viewer.ProviderCopySource = new SessionWorkspaceEntry(source.Authority, source.Metadata);
        f.Viewer.CanReviewProviderCopy.Should().BeFalse();
        f.Viewer.ProviderCopySource = f.Viewer.ProviderCopySources.Single(entry => entry.Authority.SessionId == f.Destination);
        f.Viewer.CanReviewProviderCopy.Should().BeFalse();
        await f.Viewer.ReviewProviderCopy();
        (await f.Storage.Store.ReadProviderProfile(f.Destination, f.Storage.Token)).Desired.Service.Should().Be(ProviderService.Codex);
    }

    [Fact]
    public void NativeCopyHasExplicitSourceDestinationDisclosureAndSeparateExistingConfirmRoute()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, ".github", "copilot-instructions.md"))) { root = root.Parent; }
        var controls = XDocument.Load(Path.Combine(root!.FullName, "src", "Kora", "SessionsWindow.axaml")).Descendants().ToArray();
        var picker = controls.Single(control => control.Attribute("SelectedItem")?.Value is "{Binding ProviderCopySource, Mode=TwoWay}");
        picker.Attribute("ItemsSource")!.Value.Should().Be("{Binding ProviderCopySources}");
        picker.Attribute("AutomationProperties.Name")!.Value
            .Should().Contain("Explicit exact source").And.Contain("destination");
        controls.Single(control => control.Attributes().Any(attribute => attribute.Name.LocalName is "Name" && attribute.Value is "ReviewProviderCopy"))
            .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanReviewProviderCopy}");
        controls.Single(control => control.Attributes().Any(attribute => attribute.Name.LocalName is "Name" && attribute.Value is "ConfirmProviders"))
            .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanConfirmProviders}");
    }

    private sealed class NativeCopyFixture : IAsyncDisposable
    {
        internal InteractionStorageFixture Storage { get; } = new();
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        internal AudioControlAdmission Admission { get; private set; } = null!;
        internal SessionsViewModel Viewer { get; private set; } = null!;
        internal LocalProviderProfilePreferences Preferences { get; private set; } = null!;
        internal HostId<SessionIdentity> Source { get; private set; }
        internal HostId<SessionIdentity> Destination { get; private set; }

        internal static async Task<NativeCopyFixture> Create(bool inherit)
        {
            var f = new NativeCopyFixture();
            try
            {
                await f.Storage.InitializeAsync();
                f.Source = f.Storage.Request.SessionId;
                f.Storage.Request = InteractionStorageFixture.NewRequest();
                await f.Storage.AdmitAsync(newSession: true);
                f.Destination = f.Storage.Request.SessionId;
                var workspace = WindowsSqliteSessionWorkspaceTests.Service(f.Storage, f.Access);
                f.Admission = new(f.Storage.Store, f.Storage.Store, new(f.Storage.Tasks));
                f.Preferences = new(f.Storage.Paths);
                var catalogue = new UnavailableProviderCapabilityCatalogue(new Installation(), f.Storage.Time);
                var app = new ProviderProfileConfigurationService(f.Preferences, catalogue, f.Admission, new Audit(), f.Storage.Time);
                var profiles = new SessionProviderProfileConfigurationService(workspace, f.Storage.Store, f.Preferences, catalogue, f.Storage.Time,
                    NullLogger<SessionProviderProfileConfigurationService>.Instance, app);
                if (!inherit)
                {
                    var source = await profiles.Inspect(f.Source, RequestOrigin.LocalUi, () => true, f.Storage.Token);
                    await profiles.Confirm(profiles.SelectService(source, ProviderService.Claude), RequestOrigin.LocalUi, () => true, f.Storage.Token);
                }
                var destination = await profiles.Inspect(f.Destination, RequestOrigin.LocalUi, () => true, f.Storage.Token);
                await profiles.Confirm(profiles.SelectService(destination, ProviderService.Codex), RequestOrigin.LocalUi, () => true, f.Storage.Token);
                var sink = new WindowsSqliteEvidenceSink(f.Storage.Paths);
                sink.Initialize();
                f.Viewer = new(workspace,
                    new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), f.Access, f.Storage.Time, NullLogger<DurableEvidenceQuery>.Instance),
                    f.Access, NullLogger<SessionsViewModel>.Instance, providers: profiles);
                await f.Viewer.RefreshAsync();
                await f.Viewer.SelectAsync(f.Viewer.Sessions.Single(entry => entry.Authority.SessionId == f.Destination));
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            Viewer?.Close();
            if (Admission is not null) { await Admission.DisposeAsync(); }
            Storage.Dispose();
        }
    }
}
