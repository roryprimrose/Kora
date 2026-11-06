using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalMessageOpensOnlyItsCommittedRequestAuditWithoutReplayOrActivityRenewal(bool done)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "first original request";
            await f.Model.SubmitConversationAsync();
            f.Model.ComposerBody = "second independent request";
            await f.Model.SubmitConversationAsync();
            if (done)
            {
                var target = f.Model.SelectedSessionRecord!;
                await f.Native.Sessions.ChangeLifecycleAsync(target.Authority.SessionId, target.Authority.Generation,
                    false, RequestOrigin.LocalUi, f.Native.Token);
                f.Model.SessionFilter = SessionListFilter.All;
                await f.Model.RefreshAsync();
                await f.Model.SelectAsync(f.Model.Sessions.Single(row => row.Authority.SessionId == target.Authority.SessionId));
            }
            await f.Model.ReadConversationAsync();
            var message = f.Model.ConversationMessages[0];
            var other = f.Model.ConversationMessages[1];
            var retained = await f.Native.Interactions.ReadRetentionAsync(message.Request.SessionId, f.Native.Token);
            var calls = f.Admission.Calls;
            f.Model.ComposerBody = "unchanged unsent input";
            f.Window!.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var button = f.Window.SessionView.GetVisualDescendants().OfType<Button>().SingleOrDefault(control =>
                string.Equals(control.Name, "ReadRequestAudit", StringComparison.Ordinal) && ReferenceEquals(control.DataContext, message));
            button.Should().NotBeNull("one exact retained message must offer the complete request-audit operation");
            button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Window.SessionView.Operation;
            var detail = f.Model.EvidenceDetails.Should().ContainSingle().Subject;
            detail.Source.Should().Be(EvidenceSource.AuthorityAudit);
            detail.Page.Status.Should().Be(EvidencePageStatus.Available);
            detail.Page.Records.Should().NotBeEmpty().And.OnlyContain(record =>
                record.Reference.Source == EvidenceSource.AuthorityAudit
                && record.Host!.SessionId == message.Request.SessionId
                && record.Host.RequestId == message.Request.RequestId
                && record.Host.TaskId == message.Request.TaskId);
            detail.Page.Records.Should().NotContain(record => record.Host!.RequestId == other.Request.RequestId);
            f.Window.SessionView.DetailsVisible.Should().BeTrue();
            f.Window.SessionView.FindControl<TextBlock>("DetailsHeading")!.Text.Should().Contain("request");
            f.Model.ComposerBody.Should().Be("unchanged unsent input");
            f.Admission.Calls.Should().Be(calls);
            f.Model.SelectedSessionRecord!.Authority.IsActive.Should().Be(!done);
            (await f.Native.Interactions.ReadRetentionAsync(message.Request.SessionId, f.Native.Token)).Should().Be(retained);
        });
    }

    [Fact]
    public async Task RequestAuditNextUsesTheSameSignedScopeAndRetiredControlsCannotReadAgain()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var reader = new RequestAuditReader((ICommittedAuthorityAuditReader)f.Native.Interactions) { OnePerPage = true };
            await f.Initialize(new(reader, f.Native.Access, f.Native.Time, NullLogger<DurableEvidenceQuery>.Instance));
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "request with bounded committed outcomes";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            var message = f.Model.ConversationMessages.Single();
            await f.Model.ReadRequestEvidenceAsync(message, static () => true);
            var first = f.Model.EvidenceDetails.Single();
            first.Page.Cursor.Should().NotBeNull();
            var firstBody = RequestEvidenceText(f.Window!.SessionView);
            var next = f.Window.SessionView.GetVisualDescendants().OfType<Button>()
                .Single(button => string.Equals(button.Name, "NextRequestAudit", StringComparison.Ordinal));
            next.IsEnabled.Should().BeTrue();
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Window.SessionView.Operation;
            f.Model.EvidenceDetails.Should().HaveCount(2, f.Model.ConversationStatus);
            var second = f.Model.LastEvidenceDetail!;
            second.Should().NotBeSameAs(first);
            second.Page.Records.Should().ContainSingle().Which.Reference.Should().NotBe(first.Page.Records.Single().Reference);
            firstBody.Text.Should().BeNull();
            reader.Queries.Should().HaveCount(2).And.OnlyContain(query =>
                query.Source == EvidenceSource.AuthorityAudit
                && query.SessionId == message.Request.SessionId
                && query.RequestId == message.Request.RequestId && query.TaskId == message.Request.TaskId);
            reader.Checkpoints[0].Should().BeNull();
            reader.Checkpoints[1].Should().NotBeNull();
            var displayed = RequestEvidenceText(f.Window.SessionView);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Window.SessionView.Operation;
            reader.Queries.Should().HaveCount(2, "a held old native Next cannot navigate a replacement");
            displayed.Text.Should().NotBeNull();
            var seen = new HashSet<EvidenceReference> { first.Page.Records.Single().Reference, second.Page.Records.Single().Reference };
            while (f.Model.LastEvidenceDetail!.Page.Cursor is not null)
            {
                f.Window.UpdateLayout();
                var currentNext = f.Window.SessionView.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "NextRequestAudit", StringComparison.Ordinal));
                currentNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await f.Window.SessionView.Operation;
                seen.Add(f.Model.LastEvidenceDetail!.Page.Records.Single().Reference).Should().BeTrue("snapshot pages never wrap");
            }
            f.Window.UpdateLayout();
            f.Window.SessionView.GetVisualDescendants().OfType<Button>()
                .Single(button => string.Equals(button.Name, "NextRequestAudit", StringComparison.Ordinal))
                .IsEnabled.Should().BeFalse("the bounded snapshot has actually ended");
            var reads = reader.Queries.Count;
            f.Window.SessionView.HideDetails();
            displayed.Text.Should().BeNull();
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            reader.Queries.Should().HaveCount(reads);
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("privacy")]
    [InlineData("source")]
    public async Task PendingRequestAuditJoinsItsReadAndRefusesLatePublicationAfterRetirement(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var reader = new RequestAuditReader((ICommittedAuthorityAuditReader)f.Native.Interactions);
            await f.Initialize(new(reader, f.Native.Access, f.Native.Time, NullLogger<DurableEvidenceQuery>.Instance));
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "request whose evidence is still being read";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            var message = f.Model.ConversationMessages.Single();
            f.Model.ComposerBody = "retain independent unsent input";
            f.Window!.UpdateLayout();
            var button = f.Window.SessionView.GetVisualDescendants().OfType<Button>().Single(control =>
                string.Equals(control.Name, "ReadRequestAudit", StringComparison.Ordinal));
            reader.Hold = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var operation = f.Window.SessionView.Operation;
            await reader.Entered.Task;
            try
            {
                if (string.Equals(change, "hide", StringComparison.Ordinal)) { f.Window.Hide(); }
                else if (string.Equals(change, "privacy", StringComparison.Ordinal)) { f.Native.Access.SetOpen(false); }
                else
                {
                    var target = f.Model.SelectedSessionRecord!;
                    await f.Native.Sessions.RenameAsync(target.Authority.SessionId, target.Authority.Generation,
                        target.Metadata!.Revision.Value, new("changed source"), RequestOrigin.LocalUi, f.Native.Token);
                }
                operation.IsCompleted.Should().BeFalse("revocation is not physical read completion");
                f.Model.EvidenceDetails.Should().BeEmpty();
            }
            finally { reader.Release.TrySetResult(); }
            await operation;
            f.Model.EvidenceDetails.Should().BeEmpty();
            f.Window.SessionView.DetailsVisible.Should().BeFalse();
            f.Admission.Calls.Should().Be(1);
            if (f.Native.Access.CanInspect) { f.Model.ComposerBody.Should().Be("retain independent unsent input"); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestAuditRejectsCopiedRowsAndRetiresDisplayedPrivateText(bool copied)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var reader = new RequestAuditReader((ICommittedAuthorityAuditReader)f.Native.Interactions);
            await f.Initialize(new(reader, f.Native.Access, f.Native.Time, NullLogger<DurableEvidenceQuery>.Instance));
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "exact original audit source";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            var message = f.Model.ConversationMessages.Single();
            if (copied)
            {
                await f.Model.ReadRequestEvidenceAsync(message with { }, static () => true);
                reader.Queries.Should().BeEmpty();
                f.Model.EvidenceDetails.Should().BeEmpty();
                f.Window!.SessionView.DetailsVisible.Should().BeFalse();
                f.Model.ConversationStatus.Should().Contain("unavailable or changed");
                return;
            }
            await f.Model.ReadRequestEvidenceAsync(message, static () => true);
            var body = RequestEvidenceText(f.Window!.SessionView);
            f.Native.Access.SetOpen(false);
            await f.Window.SessionView.RevalidateEvidenceAsync();
            body.Text.Should().BeNull();
            f.Window.SessionView.DetailsVisible.Should().BeFalse();
            f.Model.EvidenceDetails.Should().BeEmpty();
            f.Native.Access.SetOpen(true);
            f.Model.EvidenceDetails.Should().BeEmpty("restored private admission cannot revive an old request evidence source");
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestAuditLabelsEmptyOrUnavailableEvidenceWithoutAnotherSourceFallback(bool unavailable)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var reader = new RequestAuditReader((ICommittedAuthorityAuditReader)f.Native.Interactions)
            { Empty = true, Unavailable = unavailable };
            await f.Initialize(new(reader, f.Native.Access, f.Native.Time, NullLogger<DurableEvidenceQuery>.Instance));
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "request whose audit is not inferred";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            var message = f.Model.ConversationMessages.Single();
            await f.Model.ReadRequestEvidenceAsync(message, static () => true);
            var detail = f.Model.EvidenceDetails.Single();
            detail.Page.Records.Should().BeEmpty();
            detail.Page.Status.Should().Be(unavailable ? EvidencePageStatus.Unavailable : EvidencePageStatus.Available);
            detail.Label.Should().Contain(unavailable ? "Unavailable" : "0 records");
            var body = RequestEvidenceText(f.Window!.SessionView);
            body.Text.Should().Contain(unavailable ? "Unavailable" : "Read-only");
            await f.Model.OpenEvidenceDetailsAsync(detail);
            reader.Queries.Should().HaveCount(2).And.OnlyContain(query =>
                query.Source == EvidenceSource.AuthorityAudit && query.RequestId == message.Request.RequestId
                && query.TaskId == message.Request.TaskId && query.SessionId == message.Request.SessionId);
            if (unavailable)
            {
                body.Text.Should().BeNull();
                f.Window.SessionView.DetailsVisible.Should().BeFalse();
                f.Model.EvidenceDetails.Should().BeEmpty();
            }
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupportingConversationSurfacesOpenTheSameExactRequestAuditInThePrimaryPane(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Admission.AllowUnavailable = true;
            f.Model.ComposerBody = "request from the shared conversation row";
            await f.Model.SubmitConversationAsync();
            await f.Model.ReadConversationAsync();
            var message = f.Model.ConversationMessages.Single();
            var owner = f.Owner ?? throw new InvalidOperationException("The primary presentation owner must be composed.");
            Window surface = compact ? new CompactSessionWindow(f.Model, owner) : new SessionsWindow(f.Model, owner);
            surface.Show();
            surface.DataContext.Should().BeSameAs(f.Model);
            surface.UpdateLayout();
            var button = surface.GetVisualDescendants().OfType<Button>().Single(control =>
                string.Equals(control.Name, "ReadRequestAudit", StringComparison.Ordinal)
                && ReferenceEquals(control.DataContext, message));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await owner.DetailOperation;
            f.Model.EvidenceDetails.Should().ContainSingle().Which.Page.Records.Should().NotBeEmpty()
                .And.OnlyContain(record => record.Host!.RequestId == message.Request.RequestId);
            f.Window!.SessionView.DetailsVisible.Should().BeTrue();
            f.Admission.Calls.Should().Be(1);
        });
    }

    private static Kora.Controls.NamedTextBlock RequestEvidenceText(SessionInteractionView view)
    {
        view.UpdateLayout();
        return view.FindControl<ContentControl>("DetailsContent")!.GetVisualDescendants()
            .OfType<Kora.Controls.NamedTextBlock>().Single();
    }

    private sealed class RequestAuditReader(ICommittedAuthorityAuditReader authority) : IEvidenceReader
    {
        internal bool OnePerPage { get; init; }
        internal bool Empty { get; init; }
        internal bool Unavailable { get; init; }
        internal bool Hold { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<EvidenceQuery> Queries { get; } = [];
        internal List<EvidenceReadCheckpoint?> Checkpoints { get; } = [];

        public async ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            Checkpoints.Add(checkpoint);
            var batch = await authority.ReadAsync(OnePerPage ? query with { Limit = 1 } : query,
                checkpoint, request, now, cancellationToken);
            if (OnePerPage)
            {
                batch = batch with
                { Candidates = batch.Candidates.Take(1).ToArray(), HasMore = batch.HasMore || batch.Candidates.Count > 1 };
            }
            if (Empty)
            {
                batch = batch with
                {
                    Candidates = [], HasMore = false,
                    Status = Unavailable ? EvidencePageStatus.Unavailable : EvidencePageStatus.Available,
                    UnavailableSources = Unavailable ? [EvidenceSource.AuthorityAudit] : [],
                };
            }
            if (Hold)
            {
                Entered.TrySetResult();
#pragma warning disable VSTHRD003 // Deliberately retain this exact fake read after cancellation until the test releases it.
                await Release.Task;
#pragma warning restore VSTHRD003
            }
            return batch;
        }
    }
}
