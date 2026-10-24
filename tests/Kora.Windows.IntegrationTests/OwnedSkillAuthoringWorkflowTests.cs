using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class OwnedSkillAuthoringWorkflowTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public OwnedSkillAuthoringWorkflowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ActualDurableSkillSessionAndDirectoryWriterCompleteTwoReviewsAndInertReopen(RequestOrigin origin)
    {
        using var paths = new OwnedStorageFixture();
        var token = TestContext.Current.CancellationToken;
        var tasks = new WindowsSqliteHostTaskStore(paths);
        await tasks.InitializeAsync(token);
        var interactions = new WindowsSqliteHostInteractionStore(paths, tasks);
        await interactions.InitializeAsync(token);
        await using var admission = new OwnedSkillControlAdmission(interactions, interactions, new HostTaskCoordinator(tasks));
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var audit = new Audit();
        var workflow = new OwnedSkillSaveWorkflow(store, admission, audit);
        var authority = new OwnedSkillAuthoringAuthority(origin, static () => true);
        var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "literal only");
        var review = new NativeReview(store.Destination(document), document);
        var saved = await workflow.Save(document, authority, static () => true, review, token);
        saved.Should().NotBeNull();
        review.Reviews.Should().Be(1);
        review.Confirmations.Should().Be(1);
        saved!.Document.IsAvailableForInvocation.Should().BeFalse();
        (await workflow.List(authority, token)).Should().ContainSingle();
        var reopened = await workflow.Read("example", "1.0.0", authority, token);
        reopened!.ManifestIdentity.Should().Be(saved.ManifestIdentity);
        reopened.InstructionsIdentity.Should().Be(saved.InstructionsIdentity);
        audit.Events.Select(e => e.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(e => e.Initiator == (origin == RequestOrigin.ActivatedVoice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent().Request.RequestId.Value.Should().Be(auditEvent.CorrelationId);
            Events.Add(auditEvent);
        }
    }

    private sealed class NativeReview(string destination, OwnedDeclarativeSkillDocument document) : IOwnedSkillSaveReview
    {
        internal int Reviews { get; private set; }
        internal int Confirmations { get; private set; }
        public Task<bool> Review(OwnedDeclarativeSkillDocument proposed, string target, CancellationToken token)
        {
            Reviews++;
            proposed.Should().BeSameAs(document);
            target.Should().Be(destination);
            Directory.Exists(target).Should().BeFalse();
            return Task.FromResult(true);
        }
        public Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument proposed, string target, CancellationToken token)
        {
            Confirmations++;
            Reviews.Should().Be(1);
            proposed.Should().BeSameAs(document);
            target.Should().Be(destination);
            Directory.Exists(target).Should().BeFalse();
            return Task.FromResult(true);
        }
    }
}
