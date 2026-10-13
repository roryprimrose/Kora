using System.Text.Json.Nodes;

using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [Theory]
    [InlineData("missing-target")]
    [InlineData("missing-id")]
    [InlineData("case")]
    [InlineData("unknown")]
    [InlineData("future-version")]
    [InlineData("old-version")]
    [InlineData("runtime-target")]
    [InlineData("raw-arguments")]
    [InlineData("wrong-result-id")]
    [InlineData("wrong-result-shape")]
    [InlineData("unqualified")]
    [InlineData("denied")]
    [InlineData("oversize")]
    [InlineData("duplicate-target")]
    [InlineData("duplicate-result")]
    public async Task TargetedStoredCodecAndAuthorityRejectMalformedUnknownPartialDuplicateOrRetargetedSuccess(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var target = new SessionQueueCapabilityTarget(ReadOnlyCapabilityCatalog.Version);
        var pending = await EnqueueAsync(f, f.Request.SessionId, profile: SessionQueueReadProfile.Capability, target: target);
        var admitted = await AdmitAsync(f, pending);
        SessionQueueEntry completed;
        using (var root = HostActivity.BeginRoot(admitted.Request, HostActivityLayer.Application, HostOperation.Tool))
        {
            completed = await f.Store.CompleteAsync(admitted, SessionQueueState.Succeeded, () => true, f.Token,
                new(CapabilityOutcome.Succeeded, "admitted-read-only", Descriptor: target.Descriptor));
        }
        var canonical = HostInteractionCodec.Encode(completed);
        var json = JsonNode.Parse(canonical)!.AsObject();
        switch (boundary)
        {
            case "missing-target": json.Remove("Target"); break;
            case "missing-id": json["Target"]!.AsObject().Remove("Id"); break;
            case "case": json["Target"]!["Id"] = target.Id.ToUpperInvariant(); break;
            case "unknown": json["Target"]!["Id"] = "other"; break;
            case "future-version": json["RecordVersion"] = 6; break;
            case "old-version": json["RecordVersion"] = 4; break;
            case "runtime-target": json["Profile"] = (int)SessionQueueReadProfile.RuntimeStatus; break;
            case "raw-arguments": json["Arguments"] = "{\"id\":\"application.get_version\"}"; break;
            case "wrong-result-id": json["Result"]!["Descriptor"]!["Id"] = ReadOnlyCapabilityCatalog.List; break;
            case "wrong-result-shape": json["Result"]!["Version"] = JsonNode.Parse("{\"Version\":\"other\",\"Source\":\"fake\"}"); break;
            case "unqualified": json["Result"]!["Descriptor"]!["Effect"] = 99; break;
            case "denied": json["Result"]!["Outcome"] = (int)CapabilityOutcome.Denied; break;
            case "oversize": json["Result"]!["Reason"] = new string('x', 4096); break;
        }
        var malformed = json.ToJsonString();
        if (boundary is "duplicate-target")
        {
            malformed = malformed.Replace("\"Target\":{", "\"Target\":{\"Id\":\"other\",", StringComparison.Ordinal);
        }
        if (boundary is "duplicate-result")
        {
            malformed = malformed.Replace("\"Result\":{", "\"Result\":{\"Outcome\":0,", StringComparison.Ordinal);
        }
        var decode = () => { var value = HostInteractionCodec.Decode<SessionQueueEntry>(malformed); value.ValidateStoredResult(); };
        decode.Should().Throw<InvalidDataException>();
        using (var raw = f.OpenRaw())
        using (var command = raw.CreateCommand())
        {
            command.CommandText = "UPDATE session_queue SET payload=$payload;";
            command.Parameters.AddWithValue("$payload", malformed);
            if (boundary is "oversize")
            {
                var write = () => command.ExecuteNonQuery();
                write.Should().Throw<SqliteException>().Which.SqliteErrorCode.Should().Be(19,
                    "the unchanged physical queue row limit refuses oversized storage before authority can change");
                HostInteractionCodec.Encode((await f.Store.ReadQueueEntryAsync(f.Request.SessionId, completed.Request.TaskId, f.Token))!)
                    .Should().Be(canonical);
                return;
            }
            command.ExecuteNonQuery();
        }
        f.Reopen();
        var initialize = () => f.Store.InitializeAsync(f.Token).AsTask();
        await initialize.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Capability)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus)]
    public async Task ExactRetainedSuccessCannotAcquireDifferentTargetOrExtraResultDataAtCommit(SessionQueueReadProfile profile)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId, profile: profile, target: ProfileTarget(profile));
        var admitted = await AdmitAsync(f, pending);
        using var root = HostActivity.BeginRoot(admitted.Request, HostActivityLayer.Application, HostOperation.Tool);
        var wrong = profile == SessionQueueReadProfile.Capability
            ? new CapabilityReply(CapabilityOutcome.Succeeded, "admitted-read-only", Descriptor: ReadOnlyCapabilityCatalog.FindExact(ReadOnlyCapabilityCatalog.List))
            : new CapabilityReply(CapabilityOutcome.Succeeded, "recorded-observation",
                Runtime: new("other", RuntimeLocality.Local, CapabilityAvailability.NotObserved, null, "not observed", null, false));
        var complete = () => f.Store.CompleteAsync(admitted, SessionQueueState.Succeeded, () => true, f.Token, wrong).AsTask();
        await complete.Should().ThrowAsync<InvalidDataException>();
        var retained = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, admitted.Request.TaskId, f.Token))!;
        retained.State.Should().Be(SessionQueueState.Running);
        retained.Result.Should().BeNull();
        retained.Target.Should().Be(pending.Target);
        var retired = () => f.Store.CompleteAsync(admitted with { Target = new(ReadOnlyCapabilityCatalog.List) },
            SessionQueueState.Succeeded, () => true, f.Token, wrong).AsTask();
        await retired.Should().ThrowAsync<InvalidOperationException>();
    }
}
