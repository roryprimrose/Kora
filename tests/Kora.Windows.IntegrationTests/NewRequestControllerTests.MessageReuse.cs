using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NewRequestControllerTests
{
    private static async Task<(SessionWorkspaceEntry Source, ConversationMessage Original)> RetainOriginal(NativeUxFixtureSession f)
    {
        var source = await f.Sessions.CreateAsync(new NewRequestCreationAttempt(), () => true, f.Token);
        var request = new HostRequest(new(Guid.NewGuid()), source.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
        await f.Interactions.RetainConversationMessageAsync(request, source.Authority.Generation, source.Metadata?.Revision.Value ?? 0,
            "  retained original e\u0301🙂\r\ncomplete  ", () => true, f.Token);
        var receipt = await f.Interactions.CompleteConversationSubmissionAsync(request, ModelTurnOutcome.Unavailable,
            ModelTurnReason.UnregisteredModel, () => true, f.Token);
        var observed = await f.Interactions.ReadMetadataAsync(source.Authority.SessionId, f.Token);
        observed.Should().Be(source);
        var canonical = await f.Sessions.ReadConversationDetailAsync(observed, receipt.Message, () => true, f.Token);
        canonical.Source.Should().Be(receipt.Message.Body);
        return (observed, receipt.Message);
    }

    [WindowsFact]
    public async Task ExplicitNativeReuseConfirmationOpensOnlyOneNewRequestMemoryWindowAndNoTargetOrProvider()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var (source, original) = await RetainOriginal(f);
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            using var controller = new NewRequestWindowController(f.Main, f.Sessions, f.Access,
                () => new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance), sessions);
            controller.Bind();
            var owner = new Window();
            owner.Show();
            NewRequestWindow? target = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) => { target = view; opens++; });
            using var reviewed = Window.WindowOpenedEvent.AddClassHandler<OriginalMessageReuseWindow>((view, _) =>
                Dispatcher.UIThread.Post(() => view.GetLogicalDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "ConfirmOriginalMessageReuse", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            var before = await f.Interactions.ReadRetentionAsync(source.Authority.SessionId, f.Token);
            var count = (await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token)).Records.Length;
            try
            {
                using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
                var result = await controller.ReviewOriginalReuseAsync(source, original, owner, () => true, f.Token);
                result.Should().Contain("memory only").And.Contain("no replay");
                opens.Should().Be(1);
                var composer = ((NewRequestViewModel)target!.DataContext!).Composer;
                composer.Body.Should().Be(original.Body);
                composer.Creation.Should().BeNull();
                composer.Target.Should().BeNull();
                composer.Submission.Should().BeNull();
                composer.CanSubmit.Should().BeFalse();
                (await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token)).Records.Should().HaveCount(count);
                (await f.Interactions.ReadRetentionAsync(source.Authority.SessionId, f.Token)).Should().Be(before);
                (await f.Interactions.ReadConversationMessageAsync(source.Authority.SessionId, original.Request.RequestId, f.Token)).Should().Be(original);
                controller.RevokeSession(source.Authority.SessionId);
                composer.Body.Should().Be(original.Body, "completed explicitly confirmed memory is independent of the old source");
                controller.Open();
                opens.Should().Be(1);
                composer.Body.Should().Be(original.Body);
            }
            finally { controller.Dispose(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData("keep")]
    [InlineData("replace")]
    [InlineData("edit")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("open")]
    [InlineData("close")]
    [InlineData("repeat")]
    [InlineData("created")]
    [InlineData("owner-hide")]
    [InlineData("owner-close")]
    public async Task DirtyExistingTargetAndReentrantDecisionsNeverSilentlyOverwriteOrReviveReuse(string decision)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var (source, original) = await RetainOriginal(f);
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            using var controller = new NewRequestWindowController(f.Main, f.Sessions, f.Access,
                () => new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance), sessions);
            controller.Bind();
            var owner = new Window();
            owner.Show();
            NewRequestWindow? target = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) => target = view);
            controller.Open();
            var composer = ((NewRequestViewModel)target!.DataContext!).Composer;
            composer.Body = "existing memory";
            var prompted = 0;
            controller.DecideOriginalReuse = async (review, _) =>
            {
                prompted++;
                review.RequiresReplacement.Should().BeTrue();
                review.PreviousBody.Should().Be("existing memory");
                switch (decision)
                {
                    case "edit": composer.Body = "newer independent memory"; break;
                    case "control": f.Access.SetOpen(false); break;
                    case "source": controller.RevokeSession(source.Authority.SessionId); break;
                    case "open": controller.Open(); break;
                    case "close": target.ClosePrivate(); break;
                    case "repeat": await controller.ReviewOriginalReuseAsync(source, original, owner, () => true, f.Token); break;
                    case "owner-hide": owner.Hide(); owner.Show(); break;
                    case "owner-close": owner.Close(); break;
                }
                return decision is not "keep";
            };
            try
            {
                if (decision is "created") { await composer.Create(); }
                using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
                if (decision is not "created")
                {
                    using var sourceReview = await OriginalMessageReuseReview.ReviewAsync(f.Sessions, f.Access, source,
                        original, composer, () => true, f.Token);
                    sourceReview.Body.Should().Be(original.Body);
                }
                var result = await controller.ReviewOriginalReuseAsync(source, original, owner, () => true, f.Token);
                prompted.Should().Be(decision is "created" ? 0 : 1, result);
                composer.Body.Should().Be(decision is "replace" ? original.Body
                    : decision is "edit" ? "newer independent memory"
                    : decision is "close" ? string.Empty : "existing memory");
                composer.Submission.Should().BeNull();
                composer.CanSubmit.Should().BeFalse();
            }
            finally { controller.Dispose(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData("source")]
    [InlineData("open")]
    [InlineData("private")]
    [InlineData("close")]
    public async Task OpeningCallbacksCannotSeedRetiredSourcesOrClosedNewRequestWindows(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var (source, original) = await RetainOriginal(f);
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            NewRequestComposer? state = null;
            using var controller = new NewRequestWindowController(f.Main, f.Sessions, f.Access,
                () => state = new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance), sessions);
            controller.Bind();
            controller.DecideOriginalReuse = (_, _) => Task.FromResult(true);
            var owner = new Window();
            owner.Show();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) =>
            {
                switch (change)
                {
                    case "source": controller.RevokeSession(source.Authority.SessionId); break;
                    case "open": controller.Open(); break;
                    case "private": f.Access.SetOpen(false); break;
                    case "close": view.ClosePrivate(); break;
                }
            });
            try
            {
                using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
                var result = await controller.ReviewOriginalReuseAsync(source, original, owner, () => true, f.Token);
                result.Should().NotContain("copied to independent");
                state!.Body.Should().BeEmpty();
                state.Creation.Should().BeNull();
                state.Submission.Should().BeNull();
            }
            finally { controller.Dispose(); owner.Close(); }
        });
    }
}
