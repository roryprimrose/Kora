using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, "unchanged", false)]
    [InlineData(ArtifactKind.Instruction, "changed", true)]
    [InlineData(ArtifactKind.Prompt, "replacement", false)]
    [InlineData(ArtifactKind.Prompt, "invalid-draft", true)]
    [InlineData(ArtifactKind.Instruction, "empty-draft", false)]
    [InlineData(ArtifactKind.Prompt, "whitespace", true)]
    [InlineData(ArtifactKind.Instruction, "legacy", false)]
    [InlineData(ArtifactKind.Prompt, "v2", true)]
    [InlineData(ArtifactKind.Instruction, "equal-latest", false)]
    [InlineData(ArtifactKind.Instruction, "equal-old", false)]
    public async Task RebaseFreezesCompleteOldLatestAndRawDraftWithoutWritingOrGrantingUse(ArtifactKind kind, string state, bool bom)
    {
        await using var f = new Fixture(kind);
        var original = f.Files.Current!;
        var latest = state is "unchanged" ? original : Snapshot("Latest body", native: state is not "legacy", kind: kind);
        if (state is "replacement") { latest = original with { FileIdentity = new string('d', 48) }; }
        if (state is "v2")
        {
            latest = latest with { Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                latest.Document.Text.Replace("kora-format: 1", "kora-format: 2", StringComparison.Ordinal) + " ${input:topic}", bom),
                kind, "kora-profile", "example") };
        }
        else if (bom)
        {
            latest = latest with { Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(latest.Document.Text, true),
                kind, "kora-profile", "example") };
        }
        f.Files.Current = latest;
        var draft = state switch
        {
            "invalid-draft" => "PRIVATE_RAW_NO_HEADER \r\n \u03a9 \U0001f642  ",
            "empty-draft" => string.Empty,
            "whitespace" => "  \t\r\n \r \n",
            "equal-latest" => latest.Document.Text,
            "equal-old" => original.Document.Text,
            _ => original.Document.Text + "\r\n  PRIVATE_RAW \u03a9 \U0001f642 \r\n ",
        };
        var catalogue = f.Catalogue.Candidates;
        var preferences = f.State.Text;
        var writes = f.State.Writes;
        OwnedDraftRebase? observed = null;
        var result = await f.Actions.RebaseDraft("example", original, draft, (proposal, _) =>
        {
            observed = proposal;
            proposal.Original.Should().BeSameAs(original);
            proposal.Latest.Should().BeSameAs(latest);
            proposal.Draft.Draft.Should().Be(draft);
            proposal.Draft.RawDigest.Should().Be(ArtifactSourceComparison.Digest(ArtifactFileDocument.Encode(draft, latest.Document.HasBom)));
            proposal.Draft.RawByteCount.Should().Be(ArtifactFileDocument.Encode(draft, latest.Document.HasBom).Length);
            proposal.SameRevision.Should().Be(state is "unchanged");
            proposal.SameSourceBytes.Should().Be(state is "unchanged" or "replacement");
            proposal.Draft.ExactBytes.Should().Be(state is "equal-latest");
            proposal.SameOriginalDraftBytes.Should().Be(state is "equal-old");
            proposal.Draft.Validation.Supported.Should().Be(state is not ("invalid-draft" or "empty-draft" or "whitespace"));
            proposal.Summary.Should().Contain(original.Revision).And.Contain(latest.Revision)
                .And.Contain(original.CanonicalFileName).And.Contain(latest.Document.Definition.Id)
                .And.Contain("does not Save").And.NotContain("PRIVATE_RAW");
            if (proposal.SameSourceBytes) { proposal.ChangedFields.Should().Be("(unchanged bytes)"); }
            else { proposal.ChangedFields.Should().Contain("body"); }
            return Task.FromResult(true);
        }, NullLogger.Instance, f.Eligible, CancellationToken.None);
        result.Should().BeSameAs(observed);
        f.Files.Current.Should().BeSameAs(latest);
        f.Files.Reads.Should().Be(2);
        f.Files.Writes.Should().Be(0);
        f.Catalogue.Candidates.Should().BeSameAs(catalogue);
        f.State.Text.Should().Be(preferences);
        f.State.Writes.Should().Be(writes);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task CancelledRebasePreservesPriorBaselineAndNeverRereadsOrPublishes(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var original = f.Files.Current!;
        f.Files.Current = Snapshot("External", kind: kind);
        var result = await f.Actions.RebaseDraft("example", original, "  INVALID_RAW \r\n ", (_, _) => Task.FromResult(false),
            NullLogger.Instance, f.Eligible, CancellationToken.None);
        result.Should().BeNull();
        original.Document.Definition.Content.Should().Be("Original");
        f.Files.Reads.Should().Be(1);
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("kind")]
    [InlineData("name")]
    [InlineData("filename")]
    [InlineData("artifact")]
    [InlineData("display-name")]
    [InlineData("directory-identity")]
    [InlineData("file-identity")]
    [InlineData("unsupported")]
    [InlineData("read")]
    public async Task InvalidLatestRebaseSourceFailsClosedBeforeAnyReview(string failure)
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        f.Files.Current = failure switch
        {
            "deleted" => null,
            "kind" => Snapshot(kind: ArtifactKind.Prompt),
            "name" => original with { Name = "other" },
            "filename" => original with { CanonicalFileName = "Other.instructions.md" },
            "directory-identity" => original with { DirectoryIdentity = "unknown" },
            "file-identity" => original with { FileIdentity = "unknown" },
            "artifact" => original with { Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(original.Document.Text, false),
                ArtifactKind.Instruction, "kora-profile", "other") },
            "display-name" => original with { Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                original.Document.Text.Replace("name: example", "name: other", StringComparison.Ordinal), false),
                ArtifactKind.Instruction, "kora-profile", "example") },
            "unsupported" => original with { Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                original.Document.Text.Replace("description: Test", "description: Test\nunknown: Value", StringComparison.Ordinal), false),
                ArtifactKind.Instruction, "kora-profile", "example") },
            _ => original,
        };
        f.Files.FailRead = failure is "read";
        var reviewed = false;
        var rebase = () => f.Actions.RebaseDraft("example", original, original.Document.Text,
            (_, _) => { reviewed = true; return Task.FromResult(true); }, NullLogger.Instance, f.Eligible, CancellationToken.None);
        await rebase.Should().ThrowAsync<Exception>();
        reviewed.Should().BeFalse();
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("characters")]
    [InlineData("utf8")]
    [InlineData("unicode")]
    [InlineData("wrong-original")]
    [InlineData("unavailable")]
    [InlineData("origin")]
    public async Task RebaseNeverTruncatesOrRepairsInvalidRawAdmission(string failure)
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        var draft = failure switch
        {
            "characters" => new string('a', 65537),
            "utf8" => new string('\u4e00', 30000),
            "unicode" => "\ud800",
            _ => original.Document.Text,
        };
        if (failure is "wrong-original") { original = original with { Name = "other" }; }
        if (failure is "unavailable") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
        using var incoming = failure is "origin"
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request) : null;
        var rebase = () => f.Actions.RebaseDraft("example", original, draft, (_, _) => throw new InvalidOperationException("Must not review"),
            NullLogger.Instance, f.Eligible, CancellationToken.None);
        await rebase.Should().ThrowAsync<Exception>();
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("cancel")]
    [InlineData("generation")]
    [InlineData("catalogue-aba")]
    [InlineData("failure-aba")]
    [InlineData("deleted")]
    [InlineData("replaced")]
    [InlineData("same-id-and-version")]
    [InlineData("kind")]
    [InlineData("read")]
    public async Task RebaseConfirmationCannotAdoptChangedSourceOrExpiredReview(string change)
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        f.Files.Current = Snapshot("Latest");
        using var cancellation = new CancellationTokenSource();
        var rebase = () => f.Actions.RebaseDraft("example", original, original.Document.Text, (_, _) =>
        {
            switch (change)
            {
                case "host": f.Open = false; break;
                case "cancel": cancellation.Cancel(); break;
                case "generation": f.Control.Authority = f.Control.Authority! with { Generation = new(2) }; break;
                case "catalogue-aba":
                    var prior = f.Catalogue.Candidates;
                    f.Catalogue.Publish([.. prior.Select(item => item with { IsUseEnabled = false })]);
                    f.Catalogue.Publish(prior);
                    break;
                case "failure-aba":
                    f.Catalogue.ReportReloadFailure(new IOException("Unavailable"));
                    f.Catalogue.Publish(f.Catalogue.Candidates);
                    break;
                case "deleted": f.Files.Current = null; break;
                case "replaced": f.Files.Current = f.Files.Current! with { FileIdentity = new string('c', 48) }; break;
                case "same-id-and-version": f.Files.Current = Snapshot("Changed again, same ID and kora-format: 1"); break;
                case "kind": f.Files.Current = Snapshot(kind: ArtifactKind.Prompt); break;
                case "read": f.Files.FailRead = true; break;
            }
            return Task.FromResult(true);
        }, NullLogger.Instance, f.Eligible, cancellation.Token);
        await rebase.Should().ThrowAsync<Exception>();
        original.Document.Definition.Content.Should().Be("Original");
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("cancel")]
    [InlineData("generation")]
    [InlineData("catalogue")]
    public async Task RebaseCannotDeliverAfterAdmissionOrReadCallbacksWithdrawTheProposal(string change)
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        using var cancellation = new CancellationTokenSource();
        f.Files.BeforeRead = () =>
        {
            if (change is "host") { f.Open = false; }
            if (change is "cancel") { cancellation.Cancel(); }
            if (change is "generation") { f.Control.Authority = f.Control.Authority! with { Generation = new(2) }; }
            if (change is "catalogue") { f.Catalogue.Publish(f.Catalogue.Candidates); }
        };
        var reviewed = false;
        var rebase = () => f.Actions.RebaseDraft("example", original, original.Document.Text,
            (_, _) => { reviewed = true; return Task.FromResult(true); }, NullLogger.Instance, f.Eligible, cancellation.Token);
        await rebase.Should().ThrowAsync<Exception>();
        reviewed.Should().BeFalse();
        f.Files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("cancel")]
    [InlineData("catalogue")]
    public async Task RebaseTerminalCallbacksCannotDeliverAnExpiredObservation(string change)
    {
        await using var f = new Fixture();
        await f.Actions.List(f.Eligible, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var original = f.Files.Current!;
        f.Control.BeforeCommit = record =>
        {
            if (!record.IsTerminal) { return; }
            if (change is "host") { f.Open = false; }
            if (change is "cancel") { cancellation.Cancel(); }
            if (change is "catalogue") { f.Catalogue.Publish(f.Catalogue.Candidates); }
        };
        var rebase = () => f.Actions.RebaseDraft("example", original, original.Document.Text, (_, _) => Task.FromResult(true),
            NullLogger.Instance, f.Eligible, cancellation.Token);
        await rebase.Should().ThrowAsync<Exception>();
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task RebaseStructuralRejectionUsesTrustedCorrelatedDiagnosticsWithoutPrivateText()
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        var logger = new ValidationLogger();
        var result = await f.Actions.RebaseDraft("example", original, "PRIVATE_RAW_NO_HEADER", (_, _) => Task.FromResult(true),
            logger, f.Eligible, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Draft.Validation.Supported.Should().BeFalse();
        logger.Events.Should().NotBeEmpty();
        logger.Messages.Should().OnlyContain(message => !message.Contains("PRIVATE_RAW", StringComparison.Ordinal));
        logger.Contexts.Should().OnlyContain(context => context != null);
        logger.Contexts.Select(context => context!.Request.Origin).Should().OnlyContain(origin => origin == RequestOrigin.LocalUi);
        f.Files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task SaveAfterRebaseRequiresItsOwnExactConfirmationAndCanConflictAgain(ArtifactKind kind, bool conflict)
    {
        await using var f = new Fixture(kind);
        var original = f.Files.Current!;
        var latest = Snapshot("Latest", kind: kind);
        f.Files.Current = latest;
        var draft = Snapshot("Raw retained draft", kind: kind).Document.Text;
        var adopted = await f.Actions.RebaseDraft("example", original, draft, (_, _) => Task.FromResult(true),
            NullLogger.Instance, f.Eligible, CancellationToken.None);
        adopted.Should().NotBeNull();
        f.Files.Writes.Should().Be(0);
        if (conflict) { f.Files.Current = Snapshot("New external edit", kind: kind); }
        var review = new Review { Choice = InstructionConflictChoice.Cancel };
        var saved = await f.Workflow.Save("example", adopted!.Latest, adopted.Draft.Draft, false, review, f.Eligible, CancellationToken.None);
        review.Confirmations.Should().Be(1);
        review.Conflicts.Should().Be(conflict ? 1 : 0);
        saved.Saved.Should().Be(!conflict);
        f.Files.Current!.Document.Definition.Content.Should().Be(conflict ? "New external edit" : "Raw retained draft");
    }
}
