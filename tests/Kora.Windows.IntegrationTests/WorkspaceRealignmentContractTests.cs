using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class WorkspaceRealignmentContractTests
{
    [Theory]
    [InlineData("SessionsWindow.axaml")]
    [InlineData("CompactSessionWindow.axaml")]
    public void NormalConversationRetainsContentSearchWithoutEngineeringQueries(string file)
    {
        var controls = Names(file);
        controls.Should().Contain(["ConversationQuery", "ConversationRole", "SearchConversation",
            "ReadConversation", "ConversationSearchResults", "SubmitConversation"]);
        controls.Where(name => name.StartsWith("ConversationMinimum", StringComparison.Ordinal)
            || name.StartsWith("ConversationMaximum", StringComparison.Ordinal)
            || name.StartsWith("ConversationSubmitted", StringComparison.Ordinal)
            || name is "ConversationRequestId" or "ConversationTaskId" or "ConversationOutcome" or "ConversationReason"
                or "ConversationTaskState").Should().BeEmpty();
    }

    [Fact]
    public void SessionsRetainsExistingWorkInspectionAndCancellationWithoutDiagnosticAdmission()
    {
        var controls = Names("SessionsWindow.axaml");
        controls.Should().Contain(["ReadQueue", "InspectWork", "CancelQueueEntry", "RemoveQueueEntry",
            "ClearQueue", "ReviewPendingCancellation", "ConfirmPendingCancellation", "DiscardPendingCancellation"]);
        controls.Where(name => name.StartsWith("Enqueue", StringComparison.Ordinal)
            || name.Contains("DiagnosticsPlan", StringComparison.Ordinal)
            || name.Contains("QueueMove", StringComparison.Ordinal)
            || name.Contains("ExpiredRequeue", StringComparison.Ordinal)
            || name.StartsWith("ListMinimum", StringComparison.Ordinal)
            || name.StartsWith("ListMaximum", StringComparison.Ordinal)
            || name is "DispatchQueue" or "PauseQueue" or "ResumeQueue" or "QueuePrerequisites" or "QueueCapabilities"
                or "NoQueuePrerequisite" or "MoveQueueUp" or "MoveQueueDown" or "HistoryRequestId" or "HistoryTaskId"
                or "LocalEventTaskIdFilter" or "LocalEventQueueProfileFilter").Should().BeEmpty();
        var settings = XDocument.Load(Source("SettingsWindow.axaml"));
        settings.Descendants().Attributes().Should().NotContain(attribute =>
            attribute.Value.Contains("QueueConfiguration", StringComparison.Ordinal)
            || attribute.Value.Contains("SelectedQueuePending", StringComparison.Ordinal)
            || attribute.Value.Contains("SelectedQueueSlots", StringComparison.Ordinal)
            || attribute.Value.Contains("SelectedQueueLifetime", StringComparison.Ordinal)
            || attribute.Value.Contains("SelectedQueueActiveBudget", StringComparison.Ordinal));
        File.ReadAllText(Source("Program.cs")).Should()
            .Contain("ActivatorUtilities.CreateInstance<SessionQueueConfigurationService>(provider)")
            .And.Contain("configuration.Observe()").And.Contain("configuration.HoldUnavailable()")
            .And.Contain("configuration: provider.GetRequiredService<SessionQueueConfigurationService>()");
    }

    [Fact]
    public void AttachmentCatalogueRetainsMeaningfulSizeTypeAndFindWithoutEngineeringPredicates()
    {
        var controls = Names("AttachmentCatalogueWindow.axaml");
        controls.Should().Contain(["AttachmentMediaType", "AttachmentMinimumBytes", "AttachmentMaximumBytes",
            "AttachmentPageOrder", "MetadataDetails", "MetadataFindQuery", "OpenExact"]);
        controls.Where(name => name is "AttachmentDigest" or "AttachmentMetadataText"
            or "AttachmentFileSourceId" or "AttachmentFileRevisionId" or "AttachmentFileItemId"
            or "AttachmentObservationSource" or "AttachmentDeviceProfile" or "AttachmentCapturedFrom"
            or "AttachmentCapturedUntil" or "AttachmentGenerationRelationship"
            || name.StartsWith("AttachmentMinimumSession", StringComparison.Ordinal)
            || name.StartsWith("AttachmentMaximumSession", StringComparison.Ordinal)
            || name.StartsWith("AttachmentMinimumCaptured", StringComparison.Ordinal)
            || name.StartsWith("AttachmentMaximumCaptured", StringComparison.Ordinal)
            || name.StartsWith("AttachmentMinimumStorage", StringComparison.Ordinal)
            || name.StartsWith("AttachmentMaximumStorage", StringComparison.Ordinal)).Should().BeEmpty();
        Names("SkillPackagesWindow.axaml").Should().NotContain(["PackageDefinitionDigest", "PackageDeclaredResourceDigest"]);
        Names("SharedSkillSourcesWindow.axaml").Should().NotContain("SharedRevisionDigest");
        Names("AllWorkWindow.axaml").Should().NotContain(["WorkRanges", "RetainedTaskId", "RetainedRequestId",
            "ObservationAvailability", "ObservedContent"]);
    }

    [Fact]
    public void EvidenceTraceAndAuditQueriesRemainInTheirDiagnosticSurface()
    {
        Names("EvidenceWindow.axaml").Should().Contain(["SpanId", "TraceSource", "TraceSourceVersion",
            "ActivityName", "ActivityKind", "TraceFlags", "ParentSpanId", "OperationOutcome",
            "FromAuditSequence", "UntilAuditSequence", "FromDueUtc", "UntilDueUtc"]);
    }

    private static string[] Names(string file) => XDocument.Load(Source(file)).Descendants()
        .Attributes().Where(attribute => string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal))
        .Select(attribute => attribute.Value).ToArray();

    private static string Source(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
        { directory = directory.Parent; }
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("The source root is unavailable."),
            "src", "Kora", file);
    }
}
