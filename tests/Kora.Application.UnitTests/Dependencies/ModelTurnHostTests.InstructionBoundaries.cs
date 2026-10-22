using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("name")]
    [InlineData("revision")]
    public void ScopedInstructionChoicesExcludeNonOwnedOrIncompleteDefinitionIdentity(string fault)
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, _) = InstructionBroker(f, source);
        var original = catalogue.Candidates.Single();
        var candidate = new ArtifactDefinition(original.Id, fault is "kind" ? ArtifactKind.Skill : original.Kind,
            original.Name, original.Description, original.CommandName, original.SpokenNames,
            fault is "source" ? "external" : original.Source, original.Version, original.Digest, original.Content)
        {
            OwnedInstructionName = fault is "name" ? null : original.OwnedInstructionName,
            OwnedInstructionRevision = fault is "revision" ? null : original.OwnedInstructionRevision,
        };
        catalogue.Publish([candidate]);
        broker.Choices.Should().BeEmpty();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("content")]
    [InlineData("revision")]
    public async Task ScopedInstructionCurrentCatalogueCannotSubstituteAnyPinnedIdentityField(string fault)
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, _) = InstructionBroker(f, source);
        var original = catalogue.Candidates.Single();
        var review = await broker.ReviewAsync(new(f.Session, null), original, "Request", static () => true, Token);
        var candidate = new ArtifactDefinition(fault is "id" ? "different.id" : original.Id, original.Kind,
            original.Name, original.Description, original.CommandName, original.SpokenNames,
            fault is "source" ? "external" : original.Source, original.Version, original.Digest,
            fault is "content" ? "Altered guidance" : original.Content)
        {
            OwnedInstructionName = fault is "name" ? "another" : original.OwnedInstructionName,
            OwnedInstructionRevision = fault is "revision" ? new string('c', 227) : original.OwnedInstructionRevision,
        };
        catalogue.Publish([candidate]);
        review.IsCurrent().Should().BeFalse();
        await f.ConversationService(f.PolicyHost, execution: f.PolicyHost).Invoking(service =>
            service.SubmitInstructionAsync(review, static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedInstructionPrivateInspectionAndControlAreIndependentRequiredFences(bool inspect)
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (_, catalogue, revisions) = InstructionBroker(f, source);
        var access = new InstructionAccess();
        var broker = new ScopedInstructionUseBroker(catalogue, revisions, source, f, access, f.Time);
        var review = await broker.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(), "Request", static () => true, Token);
        if (inspect) { access.Inspect = false; }
        else { access.Control = false; }
        review.IsCurrent().Should().BeFalse();
        review.CanSubmit.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-prepared")]
    [InlineData("revoked")]
    [InlineData("already-captured")]
    public async Task ScopedInstructionCaptureIsSingleUseAndRequiresCurrentPreparedOriginalTurn(string fault)
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, revisions) = InstructionBroker(f, source);
        var review = await broker.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(), "Request", static () => true, Token);
        review.Consume();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate);
        if (fault is not "not-prepared") { review.Capture(f.Request, f.Time.Now); }
        if (fault is "revoked") { revisions.Hold(source.Snapshot!.Name); }
        if (fault is "already-captured") { review.BeginExecution(provenance); }
        ((Action)(() => review.BeginExecution(provenance))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ScopedInstructionRetiredConsumedReviewCannotConstructAnEnvelopeOrUseStaleDisplayFlags()
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, _) = InstructionBroker(f, source);
        var review = await broker.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(), "Request", static () => true, Token);
        review.Consume();
        review.Retire();
        ((Action)(() => review.Capture(f.Request, f.Time.Now))).Should().Throw<InvalidOperationException>();
        review = await broker.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(), "Request", static () => true, Token);
        review.Consume();
        review.Capture(f.Request, f.Time.Now);
        review.BeginExecution(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate));
        catalogue.Update(items => items.Select(item => item with { IsUseEnabled = false, PreservesCapturedUse = false }).ToArray());
        review.IsCurrent().Should().BeFalse();
    }

    [Fact]
    public async Task ScopedInstructionCallerAdmissionIsRequiredIndependentlyOfTheReviewSourceFence()
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, _) = InstructionBroker(f, source);
        var review = await broker.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(), "Request", static () => true, Token);
        await f.ConversationService(f.PolicyHost, execution: f.PolicyHost).Invoking(service =>
            service.SubmitInstructionAsync(review, static () => false, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
    }

    private sealed class InstructionAccess : ISessionWorkspaceAccess
    {
        internal bool Inspect { get; set; } = true;
        internal bool Control { get; set; } = true;
        public bool CanInspect => Inspect;
        public bool CanControl => Control;
        public long ControlRevision => 1;
    }

    [Fact]
    public async Task ScopedInstructionStaleEnabledDisplayFlagCannotOverrideAuthoritativeDisabledReviewState()
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, revisions) = InstructionBroker(f, source);
        revisions.Hold(source.Snapshot!.Name);
        await broker.Invoking(value => value.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(),
            "Request", static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("source-admission")]
    [InlineData("reload")]
    public async Task ScopedInstructionPendingSourceAdmissionAndCatalogueAvailabilityMustRemainCurrent(string closure)
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, _) = InstructionBroker(f, source);
        var admitted = true;
        var review = await broker.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(), "Request", () => admitted, Token);
        if (closure is "source-admission") { admitted = false; }
        else { catalogue.ReportReloadFailure(new IOException("Source unavailable")); }
        review.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task ScopedInstructionSubmitLinksLiveOriginalUserActivityToExactRetainedTurn()
    {
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, _) = InstructionBroker(f, source);
        using var root = f.Root();
        var review = await broker.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(), "Request", static () => true, Token);
        var receipt = await f.ConversationService(f.PolicyHost, execution: f.PolicyHost)
            .SubmitInstructionAsync(review, static () => true, Token);
        receipt.Message.AssistantResponse!.Body.Should().Be("answer");
        root.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task ScopedInstructionReviewWithoutATraceListenerNeverFabricatesAnActivityOrDispatches()
    {
        listener.Dispose();
        using var f = new Fixture();
        var source = new InstructionSource();
        var (broker, catalogue, _) = InstructionBroker(f, source);
        var review = await broker.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(), "Request", static () => true, Token);
        review.Trace.Should().BeNull();
        review.CanSubmit.Should().BeTrue();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }
}
