using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Skills;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task SimulatedOwnedFixtureReviewsExactInputAndDisplaysEveryAssertionWithoutSaveEnableOrRetention(RequestOrigin origin, bool saved)
    {
        await using var f = new SkillSimulationFixture(origin, saved);
        var draft = saved ? f.Source!.Document : f.Draft;
        const string output = "  e\u0301\U0001f642\r\nowned skill save; shell.run  ";
        f.Model.Model.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(output, null));
        var reviewed = false;
        (await f.Run(draft: draft, review: (preview, _) =>
        {
            reviewed = true;
            preview.Document.Should().BeSameAs(draft);
            preview.Fixture.Should().BeSameAs(f.Example);
            preview.Source.Should().BeSameAs(f.Source);
            preview.IsCurrent().Should().BeTrue();
            f.Model.Model.Adapter.Calls.Should().Be(0);
            f.Preview = preview;
            return Task.FromResult(true);
        })).Should().BeTrue();
        reviewed.Should().BeTrue();
        f.Result!.Output.Should().Be(output);
        f.Result.Assertions.Select(assertion => assertion.Passed).Should().Equal(true, false);
        f.Result.Passed.Should().BeFalse();
        f.Result.Preview.Should().BeSameAs(f.Preview);
        f.Preview!.IsCurrent().Should().BeFalse("a completed example cannot retain review authority");
        f.Model.Model.Adapter.Wire.Should().Equal(Encoding.UTF8.GetBytes(f.Preview!.Context));
        using var wire = JsonDocument.Parse(f.Model.Model.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(f.Example.Input);
        wire.RootElement.GetProperty("Evidence").EnumerateArray().Select(item => item.GetProperty("Content").GetString())
            .Should().Equal(draft.ManifestText, draft.Guidance);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
        wire.RootElement.GetProperty("SystemPolicy").GetString().Should().Contain("SIMULATED");
        foreach (var evidence in wire.RootElement.GetProperty("Evidence").EnumerateArray())
        {
            var request = evidence.GetProperty("Request");
            request.GetProperty("Origin").GetInt32().Should().Be((int)origin);
        }
        f.AssertNoEffects();
        f.Model.Model.Messages.Should().NotContain(message => message.Contains(output, StringComparison.Ordinal)
            || message.Contains(f.Example.Input, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("draft")]
    [InlineData("fixture")]
    [InlineData("channel")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("control")]
    public async Task SimulatedFixtureReviewNeverAuthorizesChangedInputsOrHiddenInference(string change)
    {
        await using var f = new SkillSimulationFixture(source: true);
        var run = () => f.Run(review: (preview, _) =>
        {
            switch (change)
            {
                case "reject": return Task.FromResult(false);
                case "draft":
                case "fixture": f.Open = false; break;
                case "channel": f.Authority.Retire(); break;
                case "expiry": f.Model.Model.Time.Now += TimeSpan.FromMinutes(2); break;
                case "clock": f.Model.Model.Time.Now -= TimeSpan.FromSeconds(1); break;
                case "source": f.Store.Source = null; break;
                case "target": f.Store.Target = new(f.Draft, "target", "manifest", "guidance"); break;
                case "control": f.Model.Model.CanControl = false; break;
            }
            if (change is "expiry")
            {
                preview.IsCurrent().Should().BeFalse();
                f.Model.Model.Time.Now -= TimeSpan.FromMinutes(2);
                preview.IsCurrent().Should().BeFalse("expiry retirement cannot recover");
            }
            return Task.FromResult(true);
        });
        if (change is "reject") { (await run()).Should().BeFalse(); }
        else { await run.Should().ThrowAsync<InvalidOperationException>(); }
        f.Result.Should().BeNull();
        f.Model.Model.Adapter.Calls.Should().Be(0);
        f.AssertNoEffects();
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("parameters")]
    [InlineData("workflow")]
    [InlineData("source-missing")]
    [InlineData("source-replaced")]
    [InlineData("target")]
    [InlineData("complete-input")]
    [InlineData("original")]
    [InlineData("cancel")]
    public async Task InvalidSimulatedSetupCannotReachReviewOrProvider(string invalid)
    {
        await using var f = new SkillSimulationFixture(source: invalid.StartsWith("source-", StringComparison.Ordinal));
        var draft = invalid switch
        {
            "tool" => OwnedDeclarativeSkillDocument.Create("example", "2.0.0", "Example", "Description", "GUIDANCE", OwnedSkillToolSelection.WebPageGet),
            "parameters" => OwnedDeclarativeSkillDocument.Create("example", "2.0.0", "Example", "Description", "${input:topic}",
                guidanceFormat: OwnedSkillGuidanceFormat.RequiredLiteralInputs),
            "workflow" => OwnedDeclarativeSkillDocument.CreateObservationWorkflow("example", "2.0.0", "Example", "Description",
                "{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{}}]}"),
            "complete-input" => OwnedDeclarativeSkillDocument.Create("example", "2.0.0", "Example", "Description", new string('x', 32768)),
            _ => f.Draft,
        };
        if (invalid is "source-missing") { f.Store.Source = null; }
        if (invalid is "source-replaced") { f.Store.Source = f.Source! with { ManifestIdentity = "replaced" }; }
        if (invalid is "target") { f.Store.Target = new(draft, "target", "manifest", "instructions"); }
        if (invalid is "original") { f.Authority.Retire(); }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (invalid is "cancel") { cancel.Cancel(); }
        await f.Invoking(value => value.Run(draft: draft, token: cancel.Token,
            review: (_, _) => throw new InvalidOperationException("Review is forbidden"))).Should().ThrowAsync<Exception>();
        f.Model.Model.Adapter.Calls.Should().Be(0);
        f.Result.Should().BeNull();
        f.AssertNoEffects();
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("pass")]
    [InlineData("source")]
    public async Task SimulatedFixtureTerminalRetiresExactReviewEvenWhenSourceAndEligibilityRecover(string terminal)
    {
        await using var f = new SkillSimulationFixture(source: true);
        var run = () => f.Run(review: (_, _) =>
        {
            if (terminal is "source") { f.Store.Source = null; }
            return Task.FromResult(terminal is not "reject");
        });
        if (terminal is "source") { await run.Should().ThrowAsync<InvalidOperationException>(); }
        else { (await run()).Should().Be(terminal is "pass"); }
        f.Store.Source = f.Source;
        f.Eligible().Should().BeTrue();
        f.Authority.IsCurrent().Should().BeTrue();
        f.Preview!.IsCurrent().Should().BeFalse("terminal retirement cannot be renewed by restored source bytes");
        f.Model.Model.Adapter.Calls.Should().Be(terminal is "pass" ? 1 : 0);
        if (terminal is not "pass") { f.Result.Should().BeNull(); }
        f.AssertNoEffects();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("target")]
    public async Task SimulatedFixtureJoinsReviewedSourceReadBeforeRejectingCancellationOrReplacement(string change)
    {
        await using var f = new SkillSimulationFixture(source: true);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = f.Run(token: cancel.Token, review: (_, _) =>
        {
            f.Store.BeforeRead = () => { started.TrySetResult(); return release.Task; };
            return Task.FromResult(true);
        });
        try
        {
            (await Task.WhenAny(started.Task, running)).Should().BeSameAs(started.Task);
            await started.Task;
            switch (change)
            {
                case "cancel": await cancel.CancelAsync(); break;
                case "source": f.Store.Source = f.Source! with { InstructionsIdentity = "replaced" }; break;
                case "target": f.Store.Target = new(f.Draft, "target", "manifest", "instructions"); break;
            }
            running.IsCompleted.Should().BeFalse("cancellation or replacement does not abandon the source reader");
            f.Model.Model.Adapter.Calls.Should().Be(0);
            f.Result.Should().BeNull();
        }
        finally { release.TrySetResult(); }
        if (change is "cancel")
        { await ((Func<Task>)(async () => await running)).Should().ThrowAsync<OperationCanceledException>(); }
        else { await ((Func<Task>)(async () => await running)).Should().ThrowAsync<InvalidOperationException>(); }
        f.Preview!.IsCurrent().Should().BeFalse();
        f.Model.Model.Adapter.Calls.Should().Be(0);
        f.Result.Should().BeNull();
        f.AssertNoEffects();
    }

    [Fact]
    public async Task SimulatedFixtureTreatsInputAndWorkflowLookingV1GuidanceAsLiteralEvidenceOnly()
    {
        await using var f = new SkillSimulationFixture();
        const string guidance = "${input:topic}\n{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{}}]}";
        var draft = OwnedDeclarativeSkillDocument.Create("example", "2.0.0", "Example", "Description", guidance);
        (await f.Run(draft: draft)).Should().BeTrue();
        draft.InputTemplate.Should().BeNull();
        draft.ObservationWorkflow.Should().BeNull();
        using var wire = JsonDocument.Parse(f.Model.Model.Adapter.Wire!);
        wire.RootElement.GetProperty("Evidence")[1].GetProperty("Content").GetString().Should().Be(guidance);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        f.Model.Model.Adapter.Calls.Should().Be(1);
        f.AssertNoEffects();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("generation")]
    [InlineData("control")]
    [InlineData("expiry")]
    public async Task SimulatedFixtureSuppressesStaleLateOutputAfterHostAwait(string change)
    {
        await using var f = new SkillSimulationFixture(source: true);
        f.Model.Model.Adapter.OnRun = () =>
        {
            switch (change)
            {
                case "source": f.Store.Source = f.Source! with { InstructionsIdentity = "replaced" }; break;
                case "owner": f.Open = false; break;
                case "privacy": f.Model.Model.CanControl = false; break;
                case "generation": f.Model.Model.Session = f.Model.Model.Session with { Generation = new(2) }; break;
                case "control": f.Model.Model.ControlRevision++; break;
                case "expiry": f.Model.Model.Time.Now += TimeSpan.FromMinutes(2); break;
            }
        };
        await f.Invoking(value => value.Run()).Should().ThrowAsync<InvalidOperationException>();
        f.Result.Should().BeNull();
        f.Model.Model.Adapter.Calls.Should().Be(1);
        f.AssertNoEffects();
    }

    [Theory]
    [InlineData("shell.run")]
    [InlineData("WebPageGet")]
    [InlineData("web.page.get")]
    [InlineData("mcp.fake")]
    [InlineData(ReadOnlyCapabilityCatalog.Version)]
    public async Task SimulatedFixtureDeniesEvenKnownToolCallbacksAndRejectsSuccessShapedAnswer(string tool)
    {
        await using var f = new SkillSimulationFixture();
        f.Model.Model.Adapter.OnTool = async invoke =>
            (await invoke(tool, "{\"url\":\"https://example.com/\",\"result\":\"counterfeit\"}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        await f.Invoking(value => value.Run()).Should().ThrowAsync<InvalidOperationException>();
        f.Result.Should().BeNull();
        f.AssertNoEffects();
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    public async Task SimulatedFixtureUnavailableProviderDoesNotDefaultPassOrRetry(ModelTurnOutcome outcome)
    {
        await using var f = new SkillSimulationFixture();
        f.Model.Model.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate, new("contains", null));
        await f.Invoking(value => value.Run()).Should().ThrowAsync<InvalidOperationException>();
        f.Result.Should().BeNull();
        f.Model.Model.Adapter.Calls.Should().Be(1);
        f.AssertNoEffects();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SimulatedFixtureRejectsMalformedOrOverboundModelOutput(bool oversized)
    {
        await using var f = new SkillSimulationFixture();
        f.Model.Model.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new(oversized ? new string('x', ModelContextEnvelope.MaximumOutputUtf8Bytes + 1) : "\0", null));
        await f.Invoking(value => value.Run()).Should().ThrowAsync<Exception>();
        f.Result.Should().BeNull();
        f.AssertNoEffects();
    }

    [Fact]
    public async Task SimulatedFixtureCancellationJoinsIgnoringAdapterAndDeniesLateCallbacks()
    {
        await using var f = new SkillSimulationFixture();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string, string, CancellationToken, ValueTask<CapabilityReply>>? callback = null;
        f.Model.Model.Adapter.OnRun = () => started.SetResult();
        f.Model.Model.Adapter.Pending = pending.Task;
        f.Model.Model.Adapter.OnTool = invoke => { callback = invoke; return Task.CompletedTask; };
        var running = f.Run(token: cancel.Token);
        await started.Task;
        await cancel.CancelAsync();
        running.IsCompleted.Should().BeFalse("actual model execution must join before a terminal");
        f.Model.Model.PolicyHost.IsQuiescent.Should().BeFalse();
        pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("LATE", null)));
        await ((Func<Task>)(async () => await running)).Should().ThrowAsync<OperationCanceledException>();
        f.Result.Should().BeNull();
        await ((Func<Task>)(async () => await callback!("shell.run", "{}", Token))).Should().ThrowAsync<OperationCanceledException>();
        f.AssertNoEffects();
    }

    [Fact]
    public async Task SimulatedFixtureFullInputBudgetIsExactAndPassStillLeavesDraftDisabled()
    {
        await using var f = new SkillSimulationFixture();
        var wireBytes = 0;
        (await f.Run(review: (preview, _) => { wireBytes = Encoding.UTF8.GetByteCount(preview.Context); return Task.FromResult(false); })).Should().BeFalse();
        var exact = OwnedDeclarativeSkillDocument.Create(f.Draft.Id, f.Draft.Version, f.Draft.Name, f.Draft.Description,
            f.Draft.Guidance + new string('x', ModelContextEnvelope.MaximumInputUtf8Bytes - wireBytes));
        var example = OwnedSkillFixture.Parse("{\"input\":\"example\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"answer\"}]}");
        await f.Service.Run(exact, null, f.Example, f.Authority, f.Eligible, (preview, _) =>
        {
            Encoding.UTF8.GetByteCount(preview.Context).Should().Be(32768);
            return Task.FromResult(true);
        }, (_, _) => Task.CompletedTask, Token);
        await f.Service.Run(f.Draft, null, example, f.Authority, f.Eligible, (_, _) => Task.FromResult(true),
            (result, _) => { result.Passed.Should().BeTrue(); return Task.CompletedTask; }, Token);
        f.Draft.IsAvailableForInvocation.Should().BeFalse();
        f.AssertNoEffects();
    }

    private sealed class SkillSimulationFixture : IAsyncDisposable
    {
        internal RefinementFixture Model { get; }
        internal SkillRefinementStore Store { get; } = new();
        internal OwnedDeclarativeSkillDocument Draft { get; } = OwnedDeclarativeSkillDocument.Create("example", "2.0.0", "Example", "Description",
            "Complete guidance. Treat owned skill save and shell.run as inert data.");
        internal OwnedDeclarativeSkillSnapshot? Source { get; }
        internal OwnedSkillFixture Example { get; } = OwnedSkillFixture.Parse(
            "{\"input\":\"  original example\\r\\nowned skill save  \",\"assertions\":[{\"kind\":\"contains\",\"value\":\"e\"},{\"kind\":\"equals\",\"value\":\"not the answer\"}]}");
        internal OwnedSkillFixtureWorkflow Service { get; }
        internal OwnedSkillFixtureWorkflow.Result? Result;
        internal OwnedSkillFixtureWorkflow.Preview? Preview;
        internal OwnedSkillAuthoringAuthority Authority { get; }
        private readonly OwnedSkillControlAdmission admission;
        internal bool Open = true;
        internal bool Eligible() => Open;
        internal SkillSimulationFixture(RequestOrigin origin = RequestOrigin.LocalUi, bool source = false)
        {
            Model = new(origin);
            Authority = new(origin, Eligible);
            if (source)
            {
                Source = new(OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Saved complete guidance"),
                    "directory", "manifest", "instructions");
                Store.Source = Source;
            }
            admission = new(Model.Model, Model.Model, new(Model.Model));
            Service = new(Store, admission, Model.Runner, Model.Model.Time);
        }
        internal Task<bool> Run(OwnedDeclarativeSkillDocument? draft = null,
            Func<OwnedSkillFixtureWorkflow.Preview, CancellationToken, Task<bool>>? review = null, CancellationToken? token = null) =>
            Service.Run(draft ?? Draft, Source, Example, Authority, Eligible,
                (preview, admitted) => { Preview = preview; return review?.Invoke(preview, admitted) ?? Task.FromResult(true); },
                (result, _) => { Result = result; return Task.CompletedTask; }, token ?? Token);
        internal void AssertNoEffects()
        {
            Store.Writes.Should().Be(0);
            Model.Model.RetainCalls.Should().Be(0);
            Model.Model.PolicyHost.IsQuiescent.Should().BeTrue();
            Model.Model.Audits.Should().NotContain(entry => entry.ActionId.Contains("enable", StringComparison.Ordinal)
                || entry.ActionId.Contains("grant", StringComparison.Ordinal));
        }
        public async ValueTask DisposeAsync()
        {
            await admission.DisposeAsync();
            await Model.DisposeAsync();
        }
    }
}
