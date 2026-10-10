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
    [Fact]
    public async Task MetadataPublicationRechecksAdmissionAfterTheServiceReadFence()
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
        source.Should().Contain("ListSearchKinds").And.Contain("NameSubstring").And.Contain("canonical lowercase exact immutable ID")
            .And.Contain("CanNextListSearch").And.Contain("including after zero matches")
            .And.Contain("CanCancelListSearch").And.Contain("Clear metadata search")
            .And.Contain("metadata-search scope across pages").And.Contain("HistoryQuery");
        var code = Read("SessionsWindow.axaml.cs");
        code.Should().Contain("SearchList.Click +=").And.Contain("model.SearchListAsync(next: true)")
            .And.Contain("model.CancelListSearch()").And.Contain("model.ClearListSearch(); ListQuery.Focus();")
            .And.Contain("ListQuery.KeyDown +=").And.Contain("model.CanSearchList");
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
        model.ListQuery = "needle";
        model.CanSearchList.Should().BeTrue();
        var search = model.SearchListAsync();
        model.CanCancelListSearch.Should().BeTrue();
        model.CanSearchList.Should().BeFalse();
        model.CanEditListSearch.Should().BeTrue();
        if (mode is "query") { model.ListQuery = "another"; }
        if (mode is "kind") { model.ListSearchKind = SessionListSearchKind.ExactId; }
        if (mode is "selection") { await model.SelectAsync(new(new(new(Guid.NewGuid()), new(1), true), null)); }
        if (mode is "source") { model.RevokeSessionList(); }
        if (mode is "cancel") { model.CancelListSearch(); }
        if (mode is "close") { model.Close(); }
        if (mode is "privacy") { access.CanInspect = false; }
        if (mode is "revision") { access.ControlRevision++; }
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
    }

    private sealed class LateListAdmission : ISessionWorkspaceAccess, IEvidenceQueryAccess
    {
        internal int RevisionReads { get; private set; }
        public bool CanInspect => true;
        public bool CanControl => false;
        public long ControlRevision => ++RevisionReads <= 4 ? 1 : 2;
    }
}
