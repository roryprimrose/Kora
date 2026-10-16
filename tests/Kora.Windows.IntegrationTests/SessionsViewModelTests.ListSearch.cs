using System.Xml.Linq;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsViewModelTests
{
    [Theory]
    [InlineData(SessionListSearchKind.NameSubstring)]
    [InlineData(SessionListSearchKind.ExactName)]
    public async Task MetadataPublicationRechecksAdmissionAfterTheServiceReadFence(SessionListSearchKind kind)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new HeldStore();
        var access = new LateListAdmission();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            model.ListSearchKind = kind;
            model.ListQuery = "needle";
            var search = model.SearchListAsync();
            held.Completion.SetResult(new([new(new(f.Request.SessionId, new(1), true),
                new(f.Request.SessionId, new(1), new("needle")))], null));
            await search;
            access.RevisionReads.Should().Be(5, "admission changed after the service's end-of-read check but before native publication");
            model.Sessions.Should().BeEmpty();
            model.SelectedSessionRecord.Should().BeNull();
            model.Status.Should().Contain("Cancelled");
            held.ControlCalls.Should().Be(0);
        }
        finally { model.Close(); }
    }

    [Fact]
    public void NativeMetadataControlsBindExplicitModesBoundedContinuationAndNoTruncationSeparatelyFromHistory()
    {
        var source = Read("SessionsWindow.axaml");
        var document = XDocument.Parse(source);
        var field = document.Descendants().Single(element => element.Name.LocalName is "TextBox"
            && element.Attribute("Text")?.Value.Contains("ListQuery", StringComparison.Ordinal) == true);
        field.Attribute("MaxLength").Should().BeNull();
        field.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanEditListSearch}");
        source.Should().Contain("ListSearchKinds").And.Contain("Browse").And.Contain("NameSubstring")
            .And.Contain("whole-name ordinal case-sensitive ExactName").And.Contain("canonical lowercase exact immutable ID")
            .And.Contain("CanNextListSearch").And.Contain("including after zero matches")
            .And.Contain("CanCancelListSearch").And.Contain("Clear metadata search")
            .And.Contain("metadata-search scope across pages").And.Contain("HistoryQuery");
        var code = Read("SessionsWindow.axaml.cs");
        code.Should().Contain("SearchList.Click +=").And.Contain("model.SearchListAsync(next: true)")
            .And.Contain("model.CancelListSearch()").And.Contain("model.ClearListSearch(); ListQuery.Focus();")
            .And.Contain("field.KeyDown +=").And.Contain("model.CanSearchList");
        var availability = document.Descendants().Single(element =>
            string.Equals(element.Attribute("ItemsSource")?.Value, "{Binding ListNameAvailabilities}", StringComparison.Ordinal));
        availability.Attribute("SelectedItem")!.Value.Should().Be("{Binding ListNameAvailability, Mode=TwoWay}");
        availability.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanEditListSearch}");
        foreach (var name in new[] { "ListMinimumGeneration", "ListMaximumGeneration",
            "ListMinimumMetadataRevision", "ListMaximumMetadataRevision" })
        {
            var range = document.Descendants().Single(element => string.Equals(
                element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value, name, StringComparison.Ordinal));
            range.Attribute("Text")!.Value.Should().Be("{Binding " + name + ", Mode=TwoWay}");
            range.Attribute("MaxLength").Should().BeNull();
            range.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanEditListSearch}");
        }
    }

    [Theory]
    [InlineData("query")]
    [InlineData("kind")]
    [InlineData("selection")]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("minimum-generation")]
    [InlineData("maximum-generation")]
    [InlineData("minimum-name-revision")]
    [InlineData("maximum-name-revision")]
    [InlineData("availability")]
    [InlineData("browse")]
    [InlineData("browse-cancel")]
    [InlineData("browse-revision")]
    [InlineData("browse-privacy")]
    [InlineData("browse-selection")]
    [InlineData("browse-availability")]
    [InlineData("browse-source")]
    [InlineData("browse-close")]
    [InlineData("exact-query")]
    [InlineData("exact-kind")]
    [InlineData("exact-selection")]
    [InlineData("exact-source")]
    [InlineData("exact-cancel")]
    [InlineData("exact-close")]
    [InlineData("exact-privacy")]
    [InlineData("exact-revision")]
    [InlineData("exact-availability")]
    [InlineData("exact-minimum-generation")]
    [InlineData("exact-maximum-generation")]
    [InlineData("exact-minimum-name-revision")]
    [InlineData("exact-maximum-name-revision")]
    public async Task MetadataSearchSuppressesLateResultsAfterQuerySelectionSourcePrivacyOrClosureChanges(string mode)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new HeldStore();
        var access = new WindowsSqliteSessionWorkspaceTests.Access { CanControl = false };
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        var browse = mode.StartsWith("browse-", StringComparison.Ordinal);
        var exact = mode.StartsWith("exact-", StringComparison.Ordinal);
        model.ListSearchKind = browse ? SessionListSearchKind.Browse
            : exact ? SessionListSearchKind.ExactName : SessionListSearchKind.NameSubstring;
        if (exact) { mode = mode["exact-".Length..]; }
        model.ListQuery = browse ? "" : "needle";
        model.ListMinimumGeneration = "1";
        model.ListMaximumGeneration = "1";
        model.ListMinimumMetadataRevision = "1";
        model.ListMaximumMetadataRevision = "1";
        model.CanSearchList.Should().BeTrue();
        var search = model.SearchListAsync();
        model.CanCancelListSearch.Should().BeTrue();
        model.CanSearchList.Should().BeFalse();
        model.CanEditListSearch.Should().BeTrue();
        if (mode is "query") { model.ListQuery = "another"; }
        if (mode is "kind") { model.ListSearchKind = SessionListSearchKind.ExactId; }
        if (mode is "browse") { model.ListSearchKind = SessionListSearchKind.Browse; }
        if (mode is "availability" or "browse-availability") { model.ListNameAvailability = SessionNameAvailability.Unnamed; }
        if (mode is "minimum-generation") { model.ListMinimumGeneration = "2"; }
        if (mode is "maximum-generation") { model.ListMaximumGeneration = "2"; }
        if (mode is "minimum-name-revision") { model.ListMinimumMetadataRevision = "2"; }
        if (mode is "maximum-name-revision") { model.ListMaximumMetadataRevision = "2"; }
        if (mode is "selection" or "browse-selection") { await model.SelectAsync(new(new(new(Guid.NewGuid()), new(1), true), null)); }
        if (mode is "source" or "browse-source") { model.RevokeSessionList(); }
        if (mode is "cancel" or "browse-cancel") { model.CancelListSearch(); }
        if (mode is "close" or "browse-close") { model.Close(); }
        if (mode is "privacy" or "browse-privacy") { access.CanInspect = false; }
        if (mode is "revision" or "browse-revision") { access.ControlRevision++; }
        var entry = new SessionWorkspaceEntry(new(f.Request.SessionId, new(1), true),
            new(f.Request.SessionId, new(1), new("needle")));
        held.Completion.SetResult(new([entry], null));
        await search;
        model.Sessions.Should().BeEmpty();
        model.Detail.Should().BeEmpty();
        model.SelectedSessionRecord.Should().BeNull();
        model.CanCancelListSearch.Should().BeFalse();
        model.CanNextListSearch.Should().BeFalse();
        model.WorkRecords.Should().BeEmpty();
        model.PendingQuestions.Should().BeEmpty();
        model.MemoryRecords.Should().BeEmpty();
        held.ControlCalls.Should().Be(0);
        held.Reads.Should().Be(1);
        model.Close();
        model.ListQuery.Should().BeEmpty();
        model.ListNameAvailability.Should().Be(SessionNameAvailability.Any);
        model.ListMinimumGeneration.Should().BeEmpty();
        model.ListMaximumGeneration.Should().BeEmpty();
        model.ListMinimumMetadataRevision.Should().BeEmpty();
        model.ListMaximumMetadataRevision.Should().BeEmpty();
    }

    private sealed class LateListAdmission : ISessionWorkspaceAccess, IEvidenceQueryAccess
    {
        internal int RevisionReads { get; private set; }
        public bool CanInspect => true;
        public bool CanControl => false;
        public long ControlRevision => ++RevisionReads <= 4 ? 1 : 2;
    }
}
