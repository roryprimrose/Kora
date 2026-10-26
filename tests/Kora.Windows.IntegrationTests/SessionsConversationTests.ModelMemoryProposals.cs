using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private const string MemoryProposal = """{"version":1,"class":"responsePreference","value":"Prefer short answers."}""";

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task ModelMemoryNativeExplicitReviewAcceptAndSeparateAdmitUsesActualSessionTransaction(int surface, bool profileMode)
    {
        await HeadlessSession.RunAsync(async () =>
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
            model.DeviceProfileMemory = profileMode;
            model.ComposerBody = "Explicitly propose one short-answer preference for this session.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
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
                f.Count("reviewed_memory").Should().Be(0, "an ordinary successful answer never creates memory");
                var reviewButton = window.GetVisualDescendants().OfType<Button>()
                    .Single(item => string.Equals(item.Name, "ReviewModelMemoryProposal", StringComparison.Ordinal));
                reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.ModelMemoryOperation;
                model.ModelMemoryProposalDescription.Should().Contain("Prefer short answers.")
                    .And.Contain(answer.Body).And.Contain("SESSION only").And.Contain("ResponsePreference")
                    .And.Contain(answer.Execution!.Turn.Value.ToString("D"));
                model.CanAcceptModelMemoryProposal.Should().BeTrue();
                model.CanAdmitModelMemoryProposal.Should().BeFalse();
                f.Count("reviewed_memory").Should().Be(0);
                async Task Click(string name)
                {
                    window.GetVisualDescendants().OfType<Button>()
                        .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await owner.ModelMemoryOperation;
                }
                await Click("AcceptModelMemoryProposal");
                model.CanAdmitModelMemoryProposal.Should().BeTrue();
                model.CanAcceptModelMemoryProposal.Should().BeFalse();
                f.Count("reviewed_memory").Should().Be(0, "acceptance does not persist the candidate");
                await Click("AdmitModelMemoryProposal");
                model.ConversationStatus.Should().Contain("SESSION memory admitted");
                model.ModelMemoryProposalDescription.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(1);
                var rows = await composed.Memories.ExecuteAsync(new(MemoryCommandOperation.List, answer.Request.SessionId.Value),
                    RequestOrigin.LocalUi, () => true, f.Token);
                var row = rows.Memories.Single();
                var inspected = await composed.Memories.ExecuteAsync(new(MemoryCommandOperation.Inspect,
                    answer.Request.SessionId.Value, row.Id, row.Revision), RequestOrigin.LocalUi, () => true, f.Token);
                inspected.Inspected!.Lineage.Request.Should().Be(answer.Request);
                inspected.Inspected.Lineage.Origin.Should().Be(MemoryProposalOrigin.Model);
                inspected.Inspected.Review.Should().Be(MemoryReviewState.Admitted);
                inspected.Inspected.Retention.Should().Be(MemoryRetentionState.Enabled);
                inspected.Inspected.Candidate!.Value.Should().Be("Prefer short answers.");
                inspected.Inspected.Scope.Should().Be(MemoryScope.Session(answer.Request.SessionId));
                (await composed.Memories.ExecuteDeviceProfileAsync(new(MemoryCommandOperation.List,
                    answer.Request.SessionId.Value), RequestOrigin.LocalUi, () => true, f.Token)).Memories.Should().BeEmpty();
                adapter.Calls.Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token))
                    .Should().Be(answer);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("reject")]
    [InlineData("refresh")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("close")]
    [InlineData("copy")]
    [InlineData("source")]
    public async Task ModelMemoryNativeRevocationCannotAdmitOrWriteLate(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f, adapter: new SummaryAnswerAdapter(MemoryProposal), memoryContext: true);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Explicitly propose session memory.";
        await model.SubmitConversationAsync();
        var source = model.ConversationMessages.Single();
        await model.ReviewModelMemoryProposalAsync(change is "copy" ? source with { } : source);
        if (change is "copy")
        {
            model.CanAcceptModelMemoryProposal.Should().BeFalse();
        }
        else if (change is "reject") { await model.RejectModelMemoryProposalAsync(); }
        else
        {
            await model.AcceptModelMemoryProposalAsync();
            switch (change)
            {
                case "cancel": model.CancelModelMemoryProposal(); break;
                case "refresh": await model.ReadConversationAsync(); break;
                case "privacy": composed.Access.CanInspect = false; break;
                case "control": composed.Access.ControlRevision++; break;
                case "close": model.Close(); break;
                case "source":
                    await composed.Workspace.ChangeLifecycleAsync(source.Request.SessionId, source.Generation,
                        false, RequestOrigin.LocalUi, f.Token);
                    break;
            }
        }
        await model.AdmitModelMemoryProposalAsync();
        f.Count("reviewed_memory").Should().Be(0);
        model.CanAdmitModelMemoryProposal.Should().BeFalse();
    }
}
