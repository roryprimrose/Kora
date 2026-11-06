using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Presentation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed partial class ExactAnswerExportWriterTests : IDisposable
{
    private readonly string root = Path.Combine(OwnedStorageFixture.ScratchParent(),
        ".scratch", "orchestration", "exact-answer-export", "native-" + Guid.NewGuid().ToString("N"));
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ExactAnswerExportWriterTests()
    {
        Directory.CreateDirectory(root);
        ActivitySource.AddActivityListener(listener);
    }
    public void Dispose() { Directory.Delete(root, recursive: true); listener.Dispose(); }
    private static HostActivity Host() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);
    private CancellationToken Token => TestContext.Current.CancellationToken;
    private WindowsExactAnswerExportWriter Writer(IExactAnswerExportCheckpoint? checkpoint = null) =>
        new(new Paths(root), NullLogger<WindowsExactAnswerExportWriter>.Instance, checkpoint);

    [Theory]
    [InlineData(".md")]
    [InlineData(".txt")]
    [InlineData(".markdown")]
    public async Task PublishesOnlyExactFullUtf8BytesWithDurableReadbackAndNoReplacement(string extension)
    {
        using var host = Host();
        var source = Source();
        var path = Path.Combine(root, "answer" + extension);
        var checkpoints = new Checkpoint();
        await using (var destination = await Writer(checkpoints).SelectAsync(path, Token))
        {
            destination.Path.Should().Be(path);
            File.Exists(path).Should().BeFalse("selection/review does not write an answer");
            var validations = 0;
            (await destination.PublishAsync(source, _ => { validations++; return Task.CompletedTask; }, () => true, Token))
                .Should().Be(ExactAnswerExportOutcome.Saved);
            validations.Should().BeGreaterThanOrEqualTo(2);
            checkpoints.Phases.Should().Equal(ExactAnswerExportPhase.BeforeStaging, ExactAnswerExportPhase.Staged,
                ExactAnswerExportPhase.Published, ExactAnswerExportPhase.Readback);
            var replay = () => destination.PublishAsync(source, _ => Task.CompletedTask, () => true, Token);
            await replay.Should().ThrowAsync<InvalidOperationException>();
        }
        var bytes = await File.ReadAllBytesAsync(path, Token);
        bytes.Should().Equal(new UTF8Encoding(false, true).GetBytes(source.Source));
        Convert.ToHexString(SHA256.HashData(bytes)).Should().Be(source.Digest);
        Encoding.UTF8.GetString(bytes).Should().EndWith("  outside viewport  ").And.NotContain("USER not exported");
        Directory.GetFiles(root).Should().Equal(path);
        var overwrite = () => Writer().SelectAsync(path, Token);
        await overwrite.Should().ThrowAsync<IOException>();
        (await File.ReadAllBytesAsync(path, Token)).Should().Equal(bytes);
    }

    [Theory]
    [InlineData(ExactAnswerExportPhase.BeforeStaging, false)]
    [InlineData(ExactAnswerExportPhase.Staged, false)]
    [InlineData(ExactAnswerExportPhase.Published, false)]
    [InlineData(ExactAnswerExportPhase.Readback, false)]
    [InlineData(ExactAnswerExportPhase.BeforeStaging, true)]
    [InlineData(ExactAnswerExportPhase.Staged, true)]
    [InlineData(ExactAnswerExportPhase.Published, true)]
    [InlineData(ExactAnswerExportPhase.Readback, true)]
    public async Task BoundaryFailuresAndCancellationAreTruthfulAndNeverRetry(ExactAnswerExportPhase phase, bool cancel)
    {
        using var host = Host();
        var path = Path.Combine(root, "answer.md");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var checkpoint = new Checkpoint
        {
            OnPhase = observed =>
            {
                if (observed != phase) { return; }
                if (cancel) { cancellation.Cancel(); }
                else { throw new IOException("synthetic phase failure"); }
            },
        };
        await using var destination = await Writer(checkpoint).SelectAsync(path, Token);
        var outcome = await destination.PublishAsync(Source(), _ => Task.CompletedTask, () => true, cancellation.Token);
        var committed = phase is ExactAnswerExportPhase.Published or ExactAnswerExportPhase.Readback;
        outcome.Should().Be(committed ? cancel ? ExactAnswerExportOutcome.Saved : ExactAnswerExportOutcome.Uncertain
            : cancel ? ExactAnswerExportOutcome.Cancelled : ExactAnswerExportOutcome.NotWritten);
        File.Exists(path).Should().Be(committed);
        Directory.GetFiles(root, ".kora-export-*").Should().BeEmpty();
        checkpoint.Phases.Count(observed => observed == phase).Should().Be(1);
        var retry = () => destination.PublishAsync(Source(), _ => Task.CompletedTask, () => true, Token);
        await retry.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ExactAnswerExportPhase.BeforeStaging, "source")]
    [InlineData(ExactAnswerExportPhase.Staged, "source")]
    [InlineData(ExactAnswerExportPhase.BeforeStaging, "private")]
    [InlineData(ExactAnswerExportPhase.Staged, "private")]
    [InlineData(ExactAnswerExportPhase.BeforeStaging, "exists")]
    [InlineData(ExactAnswerExportPhase.Staged, "exists")]
    [InlineData(ExactAnswerExportPhase.Staged, "hidden")]
    public async Task FinalValidationDeniesSourcePrivacyAndDestinationChanges(ExactAnswerExportPhase phase, string change)
    {
        using var host = Host();
        var path = Path.Combine(root, "answer.md");
        var sourceCurrent = true;
        var admitted = true;
        var checkpoint = new Checkpoint
        {
            OnPhase = observed =>
            {
                if (observed != phase) { return; }
                if (change is "source") { sourceCurrent = false; }
                if (change is "private") { admitted = false; }
                if (change is "exists") { File.WriteAllText(path, "unrelated raced file"); }
                if (change is "hidden") { File.SetAttributes(root, FileAttributes.Hidden | FileAttributes.Directory); }
            },
        };
        await using var destination = await Writer(checkpoint).SelectAsync(path, Token);
        try
        {
            var outcome = await destination.PublishAsync(Source(), _ => sourceCurrent ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException("exact source removed or revised")), () => admitted, Token);
            outcome.Should().Be(ExactAnswerExportOutcome.NotWritten);
            if (change is "exists") { (await File.ReadAllTextAsync(path, Token)).Should().Be("unrelated raced file"); }
            else { File.Exists(path).Should().BeFalse(); }
            Directory.GetFiles(root, ".kora-export-*").Should().BeEmpty();
        }
        finally { File.SetAttributes(root, FileAttributes.Directory); }
    }

    [Fact]
    public async Task HoldsEverySelectedAncestorAgainstReplacementUntilResourceClosure()
    {
        using var host = Host();
        var parent = Path.Combine(root, "held");
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, "answer.md");
        var destination = await Writer().SelectAsync(path, Token);
        var replace = () => Directory.Move(parent, parent + "-moved");
        replace.Should().Throw<IOException>();
        await destination.DisposeAsync();
        replace.Should().NotThrow();
        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("protected")]
    [InlineData("git")]
    [InlineData("generated")]
    [InlineData("stream")]
    [InlineData("unc")]
    [InlineData("alias")]
    [InlineData("hidden")]
    [InlineData("system")]
    [InlineData("unsupported")]
    [InlineData("existing")]
    [InlineData("hardlink")]
    [InlineData("directory")]
    public async Task DeniesProtectedAliasedAndAlreadyExistingDestinationsWithoutEffects(string fault)
    {
        using var host = Host();
        var parent = Path.Combine(root, fault switch { "protected" => "private-local", "git" => ".git", "generated" => "obj", _ => "ordinary" });
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, "answer.md");
        if (fault is "stream") { path += ":ads"; }
        if (fault is "unc") { path = @"\\localhost\share\answer.md"; }
        if (fault is "alias") { path = Path.Combine(parent, "..", "answer.md"); }
        if (fault is "hidden") { File.SetAttributes(parent, FileAttributes.Hidden | FileAttributes.Directory); }
        if (fault is "system") { File.SetAttributes(parent, FileAttributes.System | FileAttributes.Directory); }
        if (fault is "unsupported") { path = Path.Combine(parent, "answer.html"); }
        if (fault is "existing" or "hardlink")
        {
            await File.WriteAllTextAsync(path, "unrelated original", Token);
            if (fault is "hardlink") { CreateHardLink(Path.Combine(parent, "alias.md"), path, nint.Zero).Should().BeTrue(); }
        }
        if (fault is "directory") { Directory.CreateDirectory(path); }
        try
        {
            var select = () => Writer().SelectAsync(path, Token);
            if (fault is "existing" or "hardlink" or "directory") { await select.Should().ThrowAsync<IOException>(); }
            else { await select.Should().ThrowAsync<InvalidDataException>(); }
            Directory.GetFiles(root, ".kora-export-*", SearchOption.AllDirectories).Should().BeEmpty();
            if (fault is "existing" or "hardlink") { (await File.ReadAllTextAsync(path, Token)).Should().Be("unrelated original"); }
        }
        finally { File.SetAttributes(parent, FileAttributes.Directory); }
    }

    [Fact]
    public async Task ReparseAncestryIsRejectedWithoutWritingThroughTheJunction()
    {
        using var host = Host();
        var target = Path.Combine(root, "target");
        var alias = Path.Combine(root, "junction");
        Directory.CreateDirectory(target);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c mklink /J \"" + alias + "\" \"" + target + "\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        await process.WaitForExitAsync(Token);
        process.ExitCode.Should().Be(0);
        try
        {
            var select = () => Writer().SelectAsync(Path.Combine(alias, "answer.md"), Token);
            await select.Should().ThrowAsync<InvalidDataException>();
            Directory.GetFiles(target).Should().BeEmpty();
        }
        finally { Directory.Delete(alias); }
    }

    internal static AdmittedDetailContent Source()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var body = "# Unicode e\u0301 😀\r\n```csharp\r\nreturn 1;\r\n```\r\n"
            + new string('x', 3000) + "\n  outside viewport  ";
        var message = new ConversationMessage(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "USER not exported",
            ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, body),
        };
        return new(new(new(provenance.Turn.Value), message.Revision), DetailContentKind.Markdown,
            DetailContentOrigin.RetainedAssistant, DetailSensitivity.DisclosureConfirmationRequired,
            "ASSISTANT", "Synthetic retained answer", body, conversationMessage: message);
    }

    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => Path.Combine(root, "private-local");
        public string RoamingRoot => Path.Combine(root, "private-roaming");
    }

    private sealed class Checkpoint : IExactAnswerExportCheckpoint
    {
        internal List<ExactAnswerExportPhase> Phases { get; } = [];
        internal Action<ExactAnswerExportPhase>? OnPhase { get; init; }
        public Task ReachedAsync(ExactAnswerExportPhase phase, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Phases.Add(phase);
            OnPhase?.Invoke(phase);
            return Task.CompletedTask;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string existing, nint security);
}
