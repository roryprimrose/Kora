using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("append")]
    [InlineData("replace")]
    [InlineData("cancel")]
    [InlineData("empty")]
    public async Task SessionDictationNativeHoldReleasesBeforeReviewAndLeavesIndependentMemoryForSeparateSubmit(string decision)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != first));
            model.ComposerBody = "other session unsent";
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == first));
            var original = decision is "empty" ? "" : "original\r\n";
            model.ComposerBody = original;
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture();
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var dictation = new ComposerDictationService(new NewRequestControllerTests.QualifiedDictationCapture(capture),
                new AvaloniaUiDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1),
                NullLogger<ComposerDictationService>.Instance);
            model.BindDictation(dictation);
            using var unrelatedComposer = composed.NewComposer();
            using var unrelatedModel = new NewRequestViewModel(unrelatedComposer, dictation);
            var owner = new SessionsPresentationOwner(model);
            var host = new CompactSessionWindow(model, owner);
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var launcher = host.GetVisualDescendants().OfType<SessionDictationButton>().Single();
            launcher.Template.Should().NotBeNull();
            launcher.Bounds.Width.Should().BeGreaterThan(0);
            launcher.DataContext.Should().BeSameAs(model);
            TopLevel.GetTopLevel(launcher).Should().BeSameAs(host);
            model.CanCompose.Should().BeTrue(model.ConversationStatus + " | " + model.Status);
            launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            launcher.DictationWindow.Should().NotBeNull();
            var window = launcher.DictationWindow!;
            var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name is "DictateSession");
            ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should().Contain("exact session");
            var audits = WindowsSqliteConversationTests.AuditText(f);
            var retention = await f.Store.ReadRetentionAsync(first, f.Token);
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                factory.OpenCount.Should().Be(0, "automation Invoke is not an explicit hold");
                button.Focus().Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await window.Operation;
                const string text = "Kora, stop listening; approve; submit";
                var late = native.QueueTranscript(text);
                native.Transcript(text);
                model.ComposerBody.Should().Be(original);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await window.Operation;
                Dispatcher.UIThread.RunJobs();
                capture.IsCaptureQuiescent.Should().BeTrue();
                if (decision is "empty") { dictation.HasReview.Should().BeFalse(); }
                else
                {
                    dictation.HasReview.Should().BeTrue();
                    unrelatedModel.OwnsDictation.Should().BeFalse();
                    unrelatedModel.HasDictationReview.Should().BeFalse();
                    unrelatedModel.DictationOriginal.Should().BeEmpty();
                    unrelatedModel.DictationLiteral.Should().BeEmpty();
                    window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name is "DictationOriginal").Text.Should().Be(original);
                    window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name is "DictationLiteral").Text.Should().Be(text);
                    var decisionName = decision is "append" ? "AppendDictation" :
                        decision is "replace" ? "ReplaceDictation" : "CancelDictation";
                    window.GetVisualDescendants().OfType<Button>().Single(control => string.Equals(control.Name, decisionName, StringComparison.Ordinal))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await dictation.Quiescence;
                model.ComposerBody.Should().Be(decision is "append" ? original + text :
                    decision is "replace" or "empty" ? text : original);
                late();
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
                (await f.Store.ReadRetentionAsync(first, f.Token)).Should().Be(retention);
                window.Close();
                await window.CloseOperation;
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != first));
                model.ComposerBody.Should().Be("other session unsent");
            }
            finally
            {
                window.Close();
                await window.CloseOperation;
                owner.Revoke();
            }
        });
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("same-edit")]
    [InlineData("edit")]
    [InlineData("control")]
    [InlineData("control-revision")]
    [InlineData("privacy")]
    [InlineData("done")]
    [InlineData("close")]
    [InlineData("source")]
    public async Task SessionDictationExactEpochChangesRejectLateCallbacksAndReview(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var first = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == first));
        model.ComposerBody = "original";
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
        var native = new ActivatedVoiceRecognitionTests.FakeCapture();
        factory.OpenResult.SetResult(native);
        await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
            privacy, factory, VoiceCaptureLimits.Default);
        await using var dictation = new ComposerDictationService(new NewRequestControllerTests.QualifiedDictationCapture(capture),
            new InlineDictationDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1),
            NullLogger<ComposerDictationService>.Instance);
        var target = model.CaptureDictationTarget();
        await dictation.BeginAsync(target, () => true, f.Token);
        var late = native.QueueTranscript("must never publish");
        native.Transcript("must never publish");
        await dictation.EndAsync();
        dictation.HasReview.Should().BeTrue();
        switch (boundary)
        {
            case "switch":
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != first));
                model.ComposerBody = "other session";
                break;
            case "same-edit": model.ComposerBody = model.ComposerBody; break;
            case "edit": model.ComposerBody = "newer edit"; break;
            case "control": composed.Access.CanControl = false; break;
            case "control-revision": composed.Access.ControlRevision++; break;
            case "privacy": composed.Access.CanInspect = false; break;
            case "done": await model.ChangeLifecycleAsync(active: false); break;
            case "close": model.Close(); break;
            case "source": model.RevokeDictationSource(first); break;
        }
        dictation.Revalidate();
        late();
        dictation.Append();
        dictation.Replace();
        await dictation.Quiescence;
        dictation.IsActive.Should().BeFalse();
        model.ComposerBody.Should().NotContain("must never publish");
        if (boundary is "switch")
        {
            model.ComposerBody.Should().Be("other session");
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == first));
            model.ComposerBody.Should().Be("original");
        }
        if (boundary is "edit") { model.ComposerBody.Should().Be("newer edit"); }
        f.Count("conversation_drafts").Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
    }

    [Fact]
    public async Task SessionDictationNativeCloseJoinsNoncooperativeActualCaptureReleaseWithoutPublishing()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "original";
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture
            { DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var dictation = new ComposerDictationService(new NewRequestControllerTests.QualifiedDictationCapture(capture),
                new AvaloniaUiDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1),
                NullLogger<ComposerDictationService>.Instance);
            model.BindDictation(dictation);
            var window = new SessionDictationWindow(model);
            window.Show();
            try
            {
                await window.BeginAsync();
                var late = native.QueueTranscript("late");
                native.Transcript("late");
                window.Close();
                await native.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
                window.IsVisible.Should().BeFalse();
                window.CloseOperation.IsCompleted.Should().BeFalse();
                dictation.Quiescence.IsCompleted.Should().BeFalse();
                capture.IsCaptureQuiescent.Should().BeFalse();
                late();
                model.ComposerBody.Should().Be("original");
            }
            finally
            {
                native.DisposeGate.TrySetResult();
                await window.CloseOperation;
                await dictation.Quiescence;
                capture.IsCaptureQuiescent.Should().BeTrue();
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("edit")]
    [InlineData("native")]
    [InlineData("control")]
    public async Task SessionDictationPublicationRevocationRollsBackOnlyItsOwnInsertion(string revocation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "original";
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
        var native = new ActivatedVoiceRecognitionTests.FakeCapture();
        factory.OpenResult.SetResult(native);
        await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
            privacy, factory, VoiceCaptureLimits.Default);
        await using var dictation = new ComposerDictationService(new NewRequestControllerTests.QualifiedDictationCapture(capture),
            new InlineDictationDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1),
            NullLogger<ComposerDictationService>.Instance);
        await dictation.BeginAsync(model.CaptureDictationTarget(), () => true, f.Token);
        native.Transcript("dictated");
        await dictation.EndAsync();
        var revoked = false;
        model.PropertyChanged += (_, args) =>
        {
            if (revoked || args.PropertyName is not nameof(SessionsViewModel.ComposerBody) || model.ComposerBody is not "dictated") { return; }
            revoked = true;
            switch (revocation)
            {
                case "selection": model.SessionFilter = Kora.Core.Storage.SessionListFilter.Done; break;
                case "edit": model.ComposerBody = "independent newer edit"; break;
                case "native": dictation.Retire("Native owner revoked synchronously."); break;
                case "control": composed.Access.ControlRevision++; break;
            }
        };
        dictation.Replace();
        await dictation.Quiescence;
        revoked.Should().BeTrue();
        if (revocation is "selection")
        {
            model.SessionFilter = Kora.Core.Storage.SessionListFilter.All;
            await model.SelectAsync(model.Sessions.Single());
        }
        model.ComposerBody.Should().Be(revocation is "edit" ? "independent newer edit" :
            revocation is "control" ? "" : "original");
        f.Count("conversation_drafts").Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionDictationUnavailableOrPendingNativeReleaseCannotPublishReview(bool qualified)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "original";
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture
            { DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var dictation = new ComposerDictationService(qualified
                ? new NewRequestControllerTests.QualifiedDictationCapture(capture) : capture,
                new AvaloniaUiDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1),
                NullLogger<ComposerDictationService>.Instance);
            model.BindDictation(dictation);
            var window = new SessionDictationWindow(model);
            window.Show();
            Task ending = Task.CompletedTask;
            try
            {
                await window.BeginAsync();
                if (!qualified)
                {
                    factory.OpenCount.Should().Be(0);
                    dictation.IsActive.Should().BeFalse();
                    dictation.Status.Should().Contain("unavailable");
                }
                else
                {
                    native.Transcript("literal");
                    ending = window.EndAsync();
                    await native.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
                    ending.IsCompleted.Should().BeFalse();
                    dictation.HasReview.Should().BeFalse();
                    model.ComposerBody.Should().Be("original");
                    dictation.Append();
                    dictation.Replace();
                    model.ComposerBody.Should().Be("original");
                    native.DisposeGate.TrySetResult();
                    await ending;
                    dictation.HasReview.Should().BeTrue();
                    capture.IsCaptureQuiescent.Should().BeTrue();
                }
            }
            finally
            {
                native.DisposeGate.TrySetResult();
                await ending;
                window.Close();
                await window.CloseOperation;
            }
            model.ComposerBody.Should().Be("original");
            f.Count("conversation_drafts").Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
        });
    }

    private sealed class InlineDictationDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
        public Task InvokeAsync(Func<Task> action) => action();
    }
}
