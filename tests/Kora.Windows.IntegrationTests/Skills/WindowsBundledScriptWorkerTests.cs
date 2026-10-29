using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Definitions.Skills;
using Kora.Windows.Skills;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed class WindowsBundledScriptWorkerTests
{
    private static readonly BundledWorkerProfile Profile = new("synthetic-profile-no-executable",
        new('1', 64), "synthetic-container-no-activation", new('2', 64));

    [Fact]
    public async Task Production_readiness_is_unknown_and_launches_nothing()
    {
        var worker = new WindowsBundledScriptWorker();
        worker.QualifiedImplementationDigest.Should().BeNull();
        var act = () => worker.RunAsync(Plan(), Invocation(), static () => true, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(FixedSessionAction.Lock)]
    [InlineData(FixedSessionAction.Shutdown)]
    [InlineData(FixedSessionAction.Restart)]
    public async Task Exact_multifile_snapshot_and_one_correlated_request_complete_only_after_join(FixedSessionAction action)
    {
        var process = new Worker();
        var script = Plan(action);
        var invocation = Invocation();
        var worker = Create(process);
        var receipt = await worker.RunAsync(script, invocation, static () => true, CancellationToken.None);
        receipt.Should().Be(new BundledScriptReceipt(invocation, action, script.Package.DeclaredResourceDigest, true, true));
        process.Joined.Should().BeTrue();
        process.Disposed.Should().BeTrue();
        process.Acknowledged.Should().BeTrue();
        using var document = JsonDocument.Parse(process.Envelope!);
        var root = document.RootElement;
        var helper = root.GetProperty("Helpers").EnumerateArray().Should().ContainSingle().Subject;
        helper.GetProperty("ResourceId").GetString().Should().Be("Kora.Scripts.Shared.SessionControl");
        helper.GetProperty("Text").GetString().Should().Be(script.Package.Files.Single(item =>
            string.Equals(item.ResourceId, "Kora.Scripts.Shared.SessionControl", StringComparison.Ordinal)).Text);
        root.GetProperty("Entry").GetProperty("Text").GetString().Should().Be(script.Package.Files.Single(item =>
            string.Equals(item.Name, script.Package.Manifest.EntryPoint, StringComparison.Ordinal)).Text);
        process.Bootstrap.Should().Contain("GetProperty('Helpers')").And.Contain("[scriptblock]::Create");
    }

    [Theory]
    [InlineData("wrong-nonce")]
    [InlineData("wrong-effect")]
    [InlineData("missing-request")]
    [InlineData("missing-complete")]
    [InlineData("repeated-request")]
    [InlineData("partial-record")]
    [InlineData("overlong")]
    public async Task Malformed_unbound_or_repeated_worker_records_never_produce_a_receipt(string fault)
    {
        var process = new Worker { Fault = fault };
        var act = () => Create(process).RunAsync(Plan(), Invocation(), static () => true, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
        process.Stops.Should().BeGreaterThan(0);
        process.Joined.Should().BeTrue();
        process.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Nonzero_exit_and_overbound_error_output_are_failures_not_effect_success()
    {
        foreach (var error in new[] { false, true })
        {
            var process = new Worker { ExitCode = error ? 0 : 1 };
            if (error) { process.Error = new MemoryStream(Encoding.UTF8.GetBytes(new string('e', 8193))); }
            var act = () => Create(process).RunAsync(Plan(), Invocation(), static () => true, CancellationToken.None);
            if (error) { await act.Should().ThrowAsync<InvalidDataException>(); }
            else { await act.Should().ThrowAsync<InvalidOperationException>(); }
            process.Disposed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Retired_source_and_changed_qualification_do_not_acknowledge_worker_requests()
    {
        foreach (var qualificationChange in new[] { false, true })
        {
            var current = true;
            var profile = Profile;
            var process = new Worker();
            var worker = new WindowsBundledScriptWorker(() => profile, (_, bootstrap) =>
            {
                process.Bootstrap = bootstrap;
                if (qualificationChange) { profile = Profile with { ImplementationDigest = new('3', 64) }; }
                else { current = false; }
                return process;
            });
            var act = () => worker.RunAsync(Plan(), Invocation(), () => current, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
            process.Acknowledged.Should().BeFalse();
            process.Joined.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Invalid_origin_missing_invocation_and_cancelled_source_never_launch()
    {
        var launches = 0;
        var worker = new WindowsBundledScriptWorker(() => Profile, (_, _) =>
        { launches++; throw new InvalidOperationException("Unexpected launch."); });
        var plain = HostRequest.Create(RequestOrigin.LocalUi);
        var requests = new[]
        {
            plain,
            new HostRequest(plain.RequestId, plain.SessionId, plain.TaskId, RequestOrigin.HostSystem, new(Guid.NewGuid())),
        };
        foreach (var request in requests)
        {
            var act = () => worker.RunAsync(Plan(), request, static () => true, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        var cancelled = () => worker.RunAsync(Plan(), Invocation(), static () => true, new CancellationToken(true));
        await cancelled.Should().ThrowAsync<InvalidOperationException>();
        var retired = () => worker.RunAsync(Plan(), Invocation(), static () => false, CancellationToken.None);
        await retired.Should().ThrowAsync<InvalidOperationException>();
        launches.Should().Be(0);
    }

    [Fact]
    public async Task A_worker_with_unconfirmed_disposal_keeps_its_ownership_slot()
    {
        var process = new Worker { FailDispose = true };
        var worker = Create(process);
        var first = () => worker.RunAsync(Plan(), Invocation(), static () => true, CancellationToken.None);
        await first.Should().ThrowAsync<IOException>();
        var second = () => worker.RunAsync(Plan(), Invocation(), static () => true, CancellationToken.None);
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not joined*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_startup_is_owned_and_joined_or_retained_before_another_invocation(bool failedRelease)
    {
        var process = new Worker { FailStart = true, FailDispose = failedRelease };
        var worker = Create(process);
        await FluentActions.Awaiting(() => worker.RunAsync(Plan(), Invocation(), static () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<IOException>();
        worker.IsQuiescent.Should().Be(!failedRelease);
        process.Joined.Should().BeTrue();
        process.Acknowledged.Should().BeFalse();
        process.Envelope.Should().BeNull();
        if (failedRelease)
        {
            await FluentActions.Awaiting(() => worker.RunAsync(Plan(), Invocation(), static () => true,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        }
    }

    private static WindowsBundledScriptWorker Create(Worker process) => new(() => Profile, (_, bootstrap) =>
    { process.Bootstrap = bootstrap; return process; });

    private static BundledFixedScript Plan(FixedSessionAction action = FixedSessionAction.Lock) =>
        EmbeddedFixedActionCatalogue.Resolve(EmbeddedSkillCatalogue.Load().Packages.Single(package =>
            string.Equals(package.Manifest.Id, action switch
            {
                FixedSessionAction.Lock => "kora.session.lock",
                FixedSessionAction.Shutdown => "kora.computer.shutdown",
                _ => "kora.computer.restart",
            }, StringComparison.Ordinal)));

    private static HostRequest Invocation()
    {
        var original = HostRequest.Create(RequestOrigin.LocalUi);
        return new(original.RequestId, original.SessionId, original.TaskId, original.Origin, new(Guid.NewGuid()));
    }

    private sealed class Worker : IContainedPowerShellProcess
    {
        private readonly ResponseStream response = new();
        internal Worker() => Input = new InputStream(Receive);
        internal string? Fault { get; init; }
        internal int ExitCode { get; init; }
        internal bool FailDispose { get; init; }
        internal bool FailStart { get; init; }
        internal string? Envelope { get; private set; }
        internal string? Bootstrap { get; set; }
        internal bool Acknowledged { get; private set; }
        internal bool Joined { get; private set; }
        internal bool Disposed { get; private set; }
        internal int Stops { get; private set; }
        public Stream Input { get; }
        public Stream Output => response;
        public Stream Error { get; set; } = new MemoryStream();

        public Task StartAsync(Func<bool> current, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current().Should().BeTrue();
            if (FailStart) { throw new IOException("Synthetic failure after partial process acquisition."); }
            return Task.CompletedTask;
        }

        private void Receive(string line)
        {
            if (Envelope is not null) { Acknowledged = line.StartsWith("Kora.FixedAction.Deferred.v1|", StringComparison.Ordinal); return; }
            Envelope = line;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var nonce = root.GetProperty("Nonce").GetString();
            var id = root.GetProperty("Invocation").GetString();
            var action = root.GetProperty("Action").GetString();
            if (string.Equals(Fault, "wrong-nonce", StringComparison.Ordinal)) { nonce = "wrong"; }
            if (string.Equals(Fault, "wrong-effect", StringComparison.Ordinal)) { action = "arbitrary"; }
            var request = $"Kora.FixedAction.Request.v1|{nonce}|{id}|{action}\n";
            var complete = $"Kora.FixedAction.Completed.v1|{nonce}|{id}\n";
            var transcript = Fault switch
            {
                "missing-request" => complete,
                "missing-complete" => request,
                "repeated-request" => request + complete + request,
                "partial-record" => request.TrimEnd('\n'),
                "overlong" => new string('x', 2049),
                _ => request + complete,
            };
            response.Ready.SetResult(new MemoryStream(Encoding.UTF8.GetBytes(transcript)));
        }

        public void Stop() => Stops++;
        public Task<int> JoinAsync() { Joined = true; return Task.FromResult(ExitCode); }
        public async ValueTask DisposeAsync()
        {
            Stop();
            await JoinAsync();
            if (FailDispose) { throw new IOException("Synthetic unconfirmed process release."); }
            Disposed = true;
            Input.Dispose();
            Output.Dispose();
            Error.Dispose();
        }
    }

    private sealed class InputStream(Action<string> receive) : MemoryStream
    {
        private readonly StringBuilder pending = new();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pending.Append(Encoding.UTF8.GetString(buffer.Span));
            var text = pending.ToString();
            int newline;
            while ((newline = text.IndexOf('\n', StringComparison.Ordinal)) >= 0)
            {
                receive(text[..newline]);
                text = text[(newline + 1)..];
            }
            pending.Clear().Append(text);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ResponseStream : MemoryStream
    {
        internal readonly TaskCompletionSource<MemoryStream> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var stream = await Ready.Task.WaitAsync(cancellationToken);
            return await stream.ReadAsync(buffer, cancellationToken);
        }
    }
}