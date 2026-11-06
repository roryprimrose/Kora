using System.Collections.Immutable;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task CancelDuringModelMemoryReviewNotificationCannotRenewTheRetiredAttempt()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new SummaryAnswerAdapter(MemoryProposal);
        await using var composed = new Composition(f, adapter: adapter, memoryContext: true);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Explicitly propose session memory.";
        await model.SubmitConversationAsync();
        var source = model.ConversationMessages.Single();
        var cancelled = false;
        model.PropertyChanged += (_, args) =>
        {
            if (!cancelled && string.Equals(args.PropertyName, nameof(model.ModelMemoryProposalDescription), StringComparison.Ordinal)
                && !model.CanReadConversation)
            {
                cancelled = true;
                model.CancelModelMemoryProposal();
            }
        };

        await model.ReviewModelMemoryProposalAsync(source);

        cancelled.Should().BeTrue();
        model.ModelMemoryProposalDescription.Should().BeEmpty();
        model.CanAcceptModelMemoryProposal.Should().BeFalse();
        model.CanAdmitModelMemoryProposal.Should().BeFalse();
        f.Count("reviewed_memory").Should().Be(0);
        (await composed.Memories.ExecuteAsync(new(MemoryCommandOperation.List, source.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => true, f.Token)).Memories.Should().BeEmpty();
        f.Count("scoped_grants").Should().Be(0);
        adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancelPendingModelMemoryReviewJoinsWithoutRepublishingAndAllowsFreshSeparateAdmission(int surface)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var store = new PendingModelMemoryBoundary(f.Store);
            var adapter = new SummaryAnswerAdapter(MemoryProposal);
            await using var composed = new Composition(f, adapter: adapter, memoryContext: true, memoryStore: store);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Explicitly propose one short-answer preference for this session.";
            await model.SubmitConversationAsync();
            var source = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            Window window = surface switch
            {
                0 => new SessionsWindow(model, owner),
                1 => new CompactSessionWindow(model, owner),
                _ => new Window { DataContext = model, Content = new SessionInteractionView { DataContext = model } },
            };
            if (surface == 2) { owner.Attach(window); }
            window.Show();
            try
            {
                Button Button(string name) => window.GetVisualDescendants().OfType<Button>()
                    .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal));
                async Task Click(string name)
                {
                    Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                    await owner.ModelMemoryOperation;
                }

                store.Hold = true;
                Button("ReviewModelMemoryProposal").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                var pending = owner.ModelMemoryOperation;
                await store.Entered.Task.WaitAsync(f.Token);
                try
                {
                    pending.IsCompleted.Should().BeFalse();
                    Button("CancelModelMemoryProposal").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                    model.ModelMemoryProposalDescription.Should().BeEmpty();
                    model.CanAcceptModelMemoryProposal.Should().BeFalse();
                    model.CanAdmitModelMemoryProposal.Should().BeFalse();
                    model.CanReadConversation.Should().BeFalse("cancellation does not release pending source ownership");
                    pending.IsCompleted.Should().BeFalse();
                    f.Count("reviewed_memory").Should().Be(0);
                }
                finally
                {
                    store.Release.TrySetResult();
                    await pending;
                }

                model.CanReadConversation.Should().BeTrue("the original source worker has now joined");
                model.ModelMemoryProposalDescription.Should().BeEmpty("Cancel must retire an unpublished review too");
                model.CanAcceptModelMemoryProposal.Should().BeFalse();
                model.CanAdmitModelMemoryProposal.Should().BeFalse();
                model.ConversationStatus.Should().NotContain("Inspect the complete USER/ASSISTANT lineage");
                (await composed.Memories.ExecuteAsync(new(MemoryCommandOperation.List, source.Request.SessionId.Value),
                    RequestOrigin.LocalUi, () => true, f.Token)).Memories.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                adapter.Calls.Should().Be(1);
                (await f.Store.ReadConversationMessageAsync(source.Request.SessionId, source.Request.RequestId, f.Token))
                    .Should().Be(source);

                await Click("ReviewModelMemoryProposal");
                model.ModelMemoryProposalDescription.Should().Contain(source.Body).And.Contain("Prefer short answers.");
                model.CanAcceptModelMemoryProposal.Should().BeTrue();
                await Click("AcceptModelMemoryProposal");
                model.CanAdmitModelMemoryProposal.Should().BeTrue();
                f.Count("reviewed_memory").Should().Be(0);
                await Click("AdmitModelMemoryProposal");
                model.ConversationStatus.Should().Contain("Exact SESSION memory admitted");
                model.ModelMemoryProposalDescription.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(1);
                adapter.Calls.Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    private sealed class PendingModelMemoryBoundary(WindowsSqliteHostInteractionStore store) : IMemoryStore
    {
        private int paused;
        internal bool Hold { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<MemoryBoundary> ReadMemoryBoundaryAsync(HostRequest request, long controlRevision, CancellationToken token)
        {
            var boundary = await store.ReadMemoryBoundaryAsync(request, controlRevision, token);
            if (Hold && Interlocked.Exchange(ref paused, 1) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TestContext.Current.CancellationToken);
            }
            return boundary;
        }

        public ValueTask<MemoryResult> TransactMemoryAsync(HostRequest request, MemoryBoundary boundary,
            Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token) =>
            store.TransactMemoryAsync(request, boundary, transition, admitted, token);
    }
}
