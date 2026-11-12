using AwesomeAssertions;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.KnowledgeSources;

public sealed class SessionKnowledgeSourceBindingTests
{
    [Fact]
    public void BindingRetainsOnlyExactAssociationMetadata()
    {
        var binding = Binding();
        binding.BindingId.Should().NotBeEmpty();
        binding.ProfileId.Should().NotBeEmpty();
        binding.Session.Value.Should().NotBeEmpty();
        binding.Generation.Should().Be(new HostRevision(2));
        binding.StorageRevision.Should().Be(new HostRevision(3));
        binding.SourceVersion.Should().Be(4);
        binding.Source.SourceId.Should().NotBeEmpty();
        binding.Source.RevisionId.Should().NotBeEmpty();
        var target = Target(binding);
        var source = Source(binding);
        binding.Matches(target, source, binding.Source).Should().BeTrue();
        binding.Matches(target, source, new(binding.Source.SourceId, binding.Source.RevisionId)).Should().BeTrue();
        Binding(binding).Should().Be(binding);
    }

    [Theory]
    [InlineData(InvalidMetadata.Binding)]
    [InlineData(InvalidMetadata.Profile)]
    [InlineData(InvalidMetadata.Session)]
    [InlineData(InvalidMetadata.Generation)]
    [InlineData(InvalidMetadata.StorageRevision)]
    [InlineData(InvalidMetadata.Source)]
    [InlineData(InvalidMetadata.Revision)]
    [InlineData(InvalidMetadata.Version)]
    [InlineData(InvalidMetadata.NegativeVersion)]
    public void MalformedSavedMetadataCannotBecomeBinding(InvalidMetadata invalid)
    {
        var valid = Binding();
        FluentActions.Invoking(() => new SessionKnowledgeSourceBinding(
            invalid == InvalidMetadata.Binding ? Guid.Empty : valid.BindingId,
            invalid == InvalidMetadata.Profile ? Guid.Empty : valid.ProfileId,
            invalid == InvalidMetadata.Session ? default : valid.Session,
            invalid == InvalidMetadata.Generation ? default : valid.Generation,
            invalid == InvalidMetadata.StorageRevision ? default : valid.StorageRevision,
            new(invalid == InvalidMetadata.Source ? Guid.Empty : valid.Source.SourceId,
                invalid == InvalidMetadata.Revision ? Guid.Empty : valid.Source.RevisionId),
            invalid == InvalidMetadata.Version ? 0 : invalid == InvalidMetadata.NegativeVersion ? -1 : valid.SourceVersion))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MissingSourceReferenceIsRejectedExplicitly()
    {
        var valid = Binding();
        FluentActions.Invoking(() => new SessionKnowledgeSourceBinding(valid.BindingId, valid.ProfileId,
            valid.Session, valid.Generation, valid.StorageRevision, null!, valid.SourceVersion))
            .Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(ChangedObservation.Session)]
    [InlineData(ChangedObservation.Generation)]
    [InlineData(ChangedObservation.Done)]
    [InlineData(ChangedObservation.Source)]
    [InlineData(ChangedObservation.Version)]
    [InlineData(ChangedObservation.Disabled)]
    [InlineData(ChangedObservation.Unavailable)]
    [InlineData(ChangedObservation.Removing)]
    [InlineData(ChangedObservation.UnknownState)]
    [InlineData(ChangedObservation.Revision)]
    [InlineData(ChangedObservation.RevisionSource)]
    public void ChangedObservationNeverMatchesOrSubstitutesNewRevision(ChangedObservation changed)
    {
        var binding = Binding();
        var session = changed == ChangedObservation.Session ? new HostId<SessionIdentity>(Guid.NewGuid()) : binding.Session;
        var generation = changed == ChangedObservation.Generation ? new HostRevision(binding.Generation.Value + 1) : binding.Generation;
        var target = new SessionWorkspaceEntry(new(session, generation, changed != ChangedObservation.Done), null);
        var source = Source(binding) with
        {
            Id = changed == ChangedObservation.Source ? Guid.NewGuid() : binding.Source.SourceId,
            Version = changed == ChangedObservation.Version ? binding.SourceVersion + 1 : binding.SourceVersion,
            State = changed switch
            {
                ChangedObservation.Disabled => KnowledgeSourceState.Disabled,
                ChangedObservation.Unavailable => KnowledgeSourceState.Unavailable,
                ChangedObservation.Removing => KnowledgeSourceState.Removing,
                ChangedObservation.UnknownState => (KnowledgeSourceState)99,
                _ => KnowledgeSourceState.Enabled,
            },
        };
        var revision = new KnowledgeSourceReference(
            changed == ChangedObservation.RevisionSource ? Guid.NewGuid() : binding.Source.SourceId,
            changed == ChangedObservation.Revision ? Guid.NewGuid() : binding.Source.RevisionId);
        binding.Matches(target, source, revision).Should().BeFalse();
        binding.Matches(Target(binding), Source(binding), binding.Source).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingObservationCannotEstablishMatch(int missing)
    {
        var binding = Binding();
        FluentActions.Invoking(() => binding.Matches(missing == 0 ? null! : Target(binding),
            missing == 1 ? null! : Source(binding), missing == 2 ? null! : binding.Source))
            .Should().Throw<ArgumentNullException>();
    }

    private static SessionKnowledgeSourceBinding Binding(SessionKnowledgeSourceBinding? exact = null) =>
        exact is null
            ? new(Guid.NewGuid(), Guid.NewGuid(), new(Guid.NewGuid()), new(2), new(3),
                new(Guid.NewGuid(), Guid.NewGuid()), 4)
            : new(exact.BindingId, exact.ProfileId, exact.Session, exact.Generation, exact.StorageRevision,
                exact.Source, exact.SourceVersion);

    private static SessionWorkspaceEntry Target(SessionKnowledgeSourceBinding binding) =>
        new(new WorkSessionAuthorization(binding.Session, binding.Generation, true), null);

    private static KnowledgeSource Source(SessionKnowledgeSourceBinding binding) =>
        new(binding.Source.SourceId, binding.SourceVersion, "Documentation",
            new(@"C:\Team", "root", KnowledgeSourceScope.ImmediateFiles, [new(@"C:\Team", "root")],
                [new(@"C:\Team\guide.md", "file", 0, DateTimeOffset.UnixEpoch)]),
            KnowledgeSourceState.Enabled, DateTimeOffset.UnixEpoch, 0);

    public enum InvalidMetadata
    {
        Binding, Profile, Session, Generation, StorageRevision, Source, Revision, Version, NegativeVersion,
    }

    public enum ChangedObservation
    {
        Session, Generation, Done, Source, Version, Disabled, Unavailable, Removing, UnknownState, Revision, RevisionSource,
    }
}
