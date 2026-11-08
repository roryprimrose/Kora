using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Agents;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Definitions;

[Collection("Host tracing")]
public sealed class CustomizationAuthoringServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };
    public CustomizationAuthoringServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi, "current")]
    [InlineData(RequestOrigin.ActivatedVoice, "current")]
    [InlineData(RequestOrigin.LocalUi, "source")]
    [InlineData(RequestOrigin.LocalUi, "private")]
    [InlineData(RequestOrigin.LocalUi, "cancel")]
    [InlineData(RequestOrigin.LocalUi, "generation")]
    public async Task AgentRefinementReentersHostPublicationAfterFinalAwaitedSourceValidation(RequestOrigin origin, string change)
    {
        var store = new AudioControlTestStore();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        await using var admission = new InstructionControlAdmission(store, store, new(store));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var observed = new AuthoringRunner();
        var service = new CustomizationAuthoringService(admission, observed, TimeProvider.System);
        var source = OwnedAgentProfileDocument.ProposeToolFree("example", "", "Original role", "Original guidance").Document.Text;
        const string critique = "  Make only this guidance shorter.\r\n  ";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eligible = true;
        var checks = 0;
        async Task Validate(CancellationToken token)
        {
            checks++;
            if (!observed.Joined) { return; }
            entered.SetResult();
            await release.Task.WaitAsync(Token);
            if (change is "source") { throw new InvalidOperationException("Pinned original changed."); }
        }
        var operation = service.ProposeAgent("example", source, critique, () => eligible, cancellation.Token, Validate);
        try
        {
            if (operation.IsFaulted) { await operation; }
            (await Task.WhenAny(operation, entered.Task)).Should().BeSameAs(entered.Task);
            observed.Joined.Should().BeTrue();
            operation.IsCompleted.Should().BeFalse();
            switch (change)
            {
                case "private": eligible = false; break;
                case "cancel": cancellation.Cancel(); break;
                case "generation": store.Authority = store.Authority! with { Generation = new(2) }; break;
            }
        }
        finally { release.TrySetResult(); }
        if (change is "current")
        {
            var proposed = await operation;
            proposed.Document.Body.Should().Be("Short guidance");
            foreach (var field in OwnedAgentProfileDocument.Parse("example", source).Document.Metadata.Where(field =>
                !string.Equals(field.Key, "description", StringComparison.Ordinal)))
            { proposed.Document.Metadata[field.Key].Should().Be(field.Value); }
        }
        else if (change is "cancel")
        { await ((Func<Task>)(async () => await operation)).Should().ThrowAsync<OperationCanceledException>(); }
        else
        { await ((Func<Task>)(async () => await operation)).Should().ThrowAsync<InvalidOperationException>(); }
        checks.Should().BeGreaterThan(2);
        observed.Context!.UserRequest.Should().Be(critique);
        observed.Context.Evidence.Single().Content.Should().Be(source);
        observed.Context.ToolMode.Should().Be(ModelToolMode.None);
        observed.Joined.Should().BeTrue();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"description\":\"Role\",\"body\":\"Guidance\",\"path\":\"elsewhere\"}")]
    [InlineData("{\"description\":\"Role\",\"description\":\"Other\"}")]
    [InlineData("{\"description\":true,\"body\":\"Guidance\"}")]
    [InlineData("{\"description\":\"Role\",\"body\":null}")]
    public void AgentRefinementRejectsAnythingExceptExactlyTwoLiteralFields(string answer) =>
        ((Action)(() => CustomizationAuthoringService.ParseAgentProposal("example", "", answer)))
            .Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("same", true)]
    [InlineData("missing", false)]
    [InlineData("file", false)]
    [InlineData("directory", false)]
    [InlineData("root", false)]
    [InlineData("path", false)]
    [InlineData("bytes", false)]
    [InlineData("bom", false)]
    public void PinnedAgentSnapshotComparesCompleteIdentityAndExactBytes(string change, bool expected)
    {
        var document = OwnedAgentProfileDocument.ProposeToolFree("example", "", "Role", "Guidance");
        var original = new OwnedAgentProfileSnapshot(new("example", "owned", "directory")
            { RootBindingIdentity = "root" }, "file", document);
        var other = original with { Document = OwnedAgentProfileDocument.Parse("example", document.Document.CopyBytes()) };
        other = change switch
        {
            "missing" => null,
            "file" => other with { FileIdentity = "replacement" },
            "directory" => other with { Destination = other.Destination with { DirectoryIdentity = "replacement" } },
            "root" => other with { Destination = other.Destination with { RootBindingIdentity = "replacement" } },
            "path" => other with { Destination = other.Destination with { DisplayPath = "elsewhere" } },
            "bytes" => other with { Document = OwnedAgentProfileDocument.ProposeToolFree("example", "", "Role", "Changed") },
            "bom" => other with { Document = OwnedAgentProfileDocument.Parse("example",
                Kora.Core.Artifacts.ArtifactFileDocument.Encode(document.Document.Text, true)) },
            _ => other,
        };
        original.Matches(other).Should().Be(expected);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("read-failure")]
    [InlineData("retired-after-read")]
    public async Task ProtectedOriginalValidationNeverRenewsOrWritesThePinnedBase(string change)
    {
        var controls = new AudioControlTestStore();
        await using var admission = new InstructionControlAdmission(controls, controls, new(controls));
        var original = new OwnedAgentProfileSnapshot(new("example", "owned", "directory"), "file",
            OwnedAgentProfileDocument.ProposeToolFree("example", "", "Original", "Guidance"));
        var store = new OriginalStore(original);
        var workflow = new AgentProfileSaveWorkflow(store, admission, new NoAudit());
        var current = true;
        switch (change)
        {
            case "missing": store.Observed = null; break;
            case "changed": store.Observed = original with { FileIdentity = "replacement" }; break;
            case "read-failure": store.Failure = new IOException("Synthetic read failure"); break;
            case "retired-after-read": store.AfterRead = () => current = false; break;
        }
        var operation = () => workflow.RequireOriginalAsync(original, () => current, Token);
        if (change is "same") { await operation(); }
        else if (change is "read-failure") { await operation.Should().ThrowAsync<IOException>(); }
        else { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        original.FileIdentity.Should().Be("file");
        original.Document.Document.Body.Should().Be("Guidance");
        store.Reads.Should().Be(1);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class AuthoringRunner : IModelContextRunner
    {
        internal bool Joined;
        internal ModelContextEnvelope? Context;
        public async Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
            CancellationToken token, Func<CancellationToken, Task>? validateSource = null)
        {
            if (validateSource is not null) { await validateSource(token); }
            sourceCurrent().Should().BeTrue();
            return new(new Turn(this, target.Request, target.ExpectedGeneration!.Value));
        }

        private sealed class Turn(AuthoringRunner owner, HostRequest request, HostRevision generation) : IAdmittedModelContextTurn
        {
            public HostRequest Request => request;
            public HostRevision SessionGeneration => generation;
            public Task<LocalContextTurnReceipt> Completion => Task.FromResult(Result());
            private LocalContextTurnReceipt Result() => new(request, ModelTurnOutcome.Succeeded, LocalContextTurnReason.Completed,
                ModelResult: new(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
                    Response: new("{\"description\":\"Short role\",\"body\":\"Short guidance\"}", null)));
            public Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, CancellationToken token)
            { owner.Context = context; return Task.FromResult(Result()); }
            public ValueTask DisposeAsync() { owner.Joined = true; return ValueTask.CompletedTask; }
        }
    }

    private sealed class NoAudit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) => throw new InvalidOperationException("Source validation cannot request a write audit.");
    }

    private sealed class OriginalStore(OwnedAgentProfileSnapshot original) : IOwnedAgentProfileStore
    {
        internal OwnedAgentProfileSnapshot? Observed = original;
        internal IOException? Failure;
        internal Action? AfterRead;
        internal int Reads;
        public Task<OwnedAgentProfileSnapshot?> Read(string name, CancellationToken token)
        {
            Reads++;
            if (Failure is { } failure) { throw failure; }
            AfterRead?.Invoke();
            return Task.FromResult(Observed);
        }
        public Task<IReadOnlyList<string>> List(CancellationToken token) => throw new NotSupportedException();
        public Task<OwnedAgentProfileDestination> PrepareNew(string name, CancellationToken token) => throw new NotSupportedException();
        public Task<AgentProfileWriteResult> Create(OwnedAgentProfileDestination destination, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<AgentProfileWriteResult> Update(OwnedAgentProfileSnapshot expected, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
