using System.Collections.Immutable;
using System.Globalization;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeRangesAreSharedAccessibleInclusivePassiveAndRetireSelectedRowsWithoutReading()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker Denied");
            var snapshot = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 50, f.Token);
            var messages = Enumerable.Range(1, 30).Select(index => snapshot.Messages[0] with
            {
                Sequence = index,
                SubmittedAt = DateTimeOffset.UnixEpoch.AddTicks(index),
            }).ToImmutableArray();
            var reader = new SearchReader((_, cursor, limit, _) => ValueTask.FromResult(snapshot with
            {
                Messages = messages.Where(message => message.Sequence > (cursor?.After ?? 0)).Take(limit).ToImmutableArray(),
                Ceiling = 30,
                HeadRevision = 60,
            }));
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "independent dirty";
                model.ConversationQuery = "marker";
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                foreach (var (name, value) in new[]
                {
                    ("ConversationMinimumSequence", "1"), ("ConversationMaximumSequence", "30"),
                    ("ConversationMinimumGeneration", "1"), ("ConversationMaximumGeneration", "9223372036854775807"),
                    ("ConversationMinimumRevision", "1"), ("ConversationMaximumRevision", "2"),
                    ("ConversationSubmittedFrom", DateTimeOffset.UnixEpoch.AddTicks(1).ToString("O", CultureInfo.InvariantCulture)),
                    ("ConversationSubmittedThrough", DateTimeOffset.UnixEpoch.AddTicks(30).ToString("O", CultureInfo.InvariantCulture)),
                })
                {
                    model.ConversationQuery = "marker";
                    await model.SearchConversationAsync();
                    model.CanNextConversationSearch.Should().BeTrue();
                    var results = workspace.FindControl<ListBox>("ConversationSearchResults")!;
                    var sharedResults = compact.FindControl<ListBox>("ConversationSearchResults")!;
                    results.SelectedIndex = 0;
                    sharedResults.SelectedIndex = 0;
                    var control = workspace.FindControl<TextBox>(name)!;
                    var shared = compact.FindControl<TextBox>(name)!;
                    AutomationProperties.GetName(control).Should().Contain("Inclusive").And.Contain("empty means Any").And.Contain("editing does not read");
                    AutomationProperties.GetName(shared).Should().Contain("Shared inclusive");
                    control.Focusable.Should().BeTrue();
                    shared.Focusable.Should().BeTrue();
                    var reads = reader.Reads;
                    shared.Text = value;
                    control.Text.Should().Be(value);
                    reader.Reads.Should().Be(reads);
                    model.ConversationSearchMessages.Should().BeEmpty();
                    results.SelectedItem.Should().BeNull();
                    sharedResults.SelectedItem.Should().BeNull();
                    model.CanNextConversationSearch.Should().BeFalse();
                    if (name.Contains("Generation", StringComparison.Ordinal) || name.Contains("Revision", StringComparison.Ordinal))
                    {
                        foreach (var source in new[] { control, shared })
                        {
                            source.Text = value;
                            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                            source.RaiseEvent(escape);
                            escape.Handled.Should().BeTrue();
                            source.Text.Should().BeEmpty();
                            reader.Reads.Should().Be(reads);
                            compact.IsVisible.Should().BeTrue();
                            workspace.IsVisible.Should().BeTrue();
                        }
                    }
                }
                model.ConversationQuery = "marker";
                model.ConversationMinimumSequence = "2";
                model.ConversationMaximumSequence = "29";
                model.ConversationSubmittedFrom = DateTimeOffset.UnixEpoch.AddTicks(3).ToString("O", CultureInfo.InvariantCulture);
                model.ConversationSubmittedThrough = DateTimeOffset.UnixEpoch.AddTicks(28).ToString("O", CultureInfo.InvariantCulture);
                model.ConversationOutcome = ModelTurnOutcome.Unavailable;
                model.ConversationReason = ModelTurnReason.UnregisteredModel;
                Click(compact, "SearchConversation");
                await compact.ConversationOperation;
                model.ConversationSearchMessages.Should().Equal(messages.Skip(2).Take(25));
                Click(workspace, "NextConversationSearch");
                await workspace.ConversationOperation;
                model.ConversationSearchMessages.Should().Equal(messages[27]);
                model.CanNextConversationSearch.Should().BeFalse();
                model.ConversationRole = ConversationSearchRole.HOST;
                model.ConversationQuery = "Denied";
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty("status-looking USER words never become HOST metadata");
                Click(compact, "ClearConversationSearch");
                model.ConversationMinimumSequence.Should().BeEmpty();
                model.ConversationMaximumSequence.Should().BeEmpty();
                model.ConversationSubmittedFrom.Should().BeEmpty();
                model.ConversationSubmittedThrough.Should().BeEmpty();
                model.ConversationMinimumGeneration.Should().BeEmpty();
                model.ConversationMaximumGeneration.Should().BeEmpty();
                model.ConversationMinimumRevision.Should().BeEmpty();
                model.ConversationMaximumRevision.Should().BeEmpty();
                model.ComposerBody.Should().Be("independent dirty");
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            }

            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("0", "", "", "")]
    [InlineData("", "0", "", "")]
    [InlineData("2", "1", "", "")]
    [InlineData("+1", "", "", "")]
    [InlineData("", " 1", "", "")]
    [InlineData("9223372036854775808", "", "", "")]
    [InlineData("", "", "0", "")]
    [InlineData("", "", "", "4")]
    [InlineData("", "", "2", "1")]
    [InlineData("", "", "", "2 ")]
    public async Task InvalidNativeProvenanceFailsWithoutReadsOrDraftMutation(string minimum, string maximum, string revisionFrom, string revisionThrough)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var reader = new SearchReader((_, _, _, _) => throw new InvalidOperationException("must not read"));
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "independent draft";
        model.ConversationQuery = "marker";
        model.ConversationMinimumGeneration = minimum;
        model.ConversationMaximumGeneration = maximum;
        model.ConversationMinimumRevision = revisionFrom;
        model.ConversationMaximumRevision = revisionThrough;
        await model.SearchConversationAsync();
        reader.Reads.Should().Be(0);
        model.ConversationSearchMessages.Should().BeEmpty();
        model.ConversationSearchStatus.Should().Contain("InvalidRange").And.Contain("revision 1-3");
        model.ConversationMinimumGeneration.Should().Be(minimum);
        model.ConversationMaximumGeneration.Should().Be(maximum);
        model.ConversationMinimumRevision.Should().Be(revisionFrom);
        model.ConversationMaximumRevision.Should().Be(revisionThrough);
        model.ComposerBody.Should().Be("independent draft");
    }
    [Theory]
    [InlineData("0", "", "", "")]
    [InlineData("", "101", "", "")]
    [InlineData("2", "1", "", "")]
    [InlineData("+1", "", "", "")]
    [InlineData("", "", "1970-01-01T00:00:00Z", "")]
    [InlineData("", "", "1970-01-01T00:00:00.0000000+01:00", "")]
    [InlineData("", "", "1970-01-02T00:00:00.0000000+00:00", "1970-01-01T00:00:00.0000000+00:00")]
    public async Task InvalidNativeRangesExplicitlyFailBeforeAnyBodyRead(string minimum, string maximum, string from, string through)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var reader = new SearchReader((_, _, _, _) => throw new InvalidOperationException("must not read"));
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ConversationQuery = "marker";
        model.ConversationMinimumSequence = minimum;
        model.ConversationMaximumSequence = maximum;
        model.ConversationSubmittedFrom = from;
        model.ConversationSubmittedThrough = through;
        await model.SearchConversationAsync();
        reader.Reads.Should().Be(0);
        model.ConversationSearchMessages.Should().BeEmpty();
        model.ConversationSearchStatus.Should().Contain("InvalidRange").And.Contain("empty means Any");
        model.ConversationMinimumSequence.Should().Be(minimum);
        model.ConversationMaximumSequence.Should().Be(maximum);
        model.ConversationSubmittedFrom.Should().Be(from);
        model.ConversationSubmittedThrough.Should().Be(through);
    }
}