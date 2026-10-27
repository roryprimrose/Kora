using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class ClaudeAccountBrokerTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ClaudeAccountBrokerTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("approval")]
    [InlineData("lease")]
    [InlineData("voice")]
    [InlineData("privacy")]
    public async Task No_approval_or_native_credential_binding_means_zero_network(string fault)
    {
        var f = new Fixture { Approved = fault is not "approval", Bound = fault is not "lease" };
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(fault is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
            HostActivityLayer.Windows, HostOperation.Request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.SignInAsync(Fixture.Credential, () => fault is not "privacy", Token));
        f.Requests.Should().BeEmpty();
        (await broker.ObserveAsync(Token)).Models.Should().BeEmpty();
        broker.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Shipped_approval_source_is_explicitly_unavailable_not_an_authentication_or_readiness_claim()
    {
        var f = new Fixture();
        var source = new UnavailableClaudeDeploymentApprovalSource();
        (await source.ResolveAsync(Token)).Should().BeNull();
        await using var broker = new ClaudeAccountBroker(source, f.Time, f, NullLogger<ClaudeAccountBroker>.Instance, f.CreateHttp);
        (await broker.CanSetUpAsync(Token)).Should().BeFalse();
        var catalogue = await broker.ObserveAsync(Token);
        catalogue.Status.Should().Be(ProviderCatalogueStatus.Unavailable);
        catalogue.Models.Should().BeEmpty();
        f.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Exact_credential_and_workspace_authenticate_without_inference_and_revoke_invalidates_catalogue()
    {
        var f = new Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        catalogue.Service.Should().Be(ProviderService.Claude);
        catalogue.Account.Should().Be(f.Lease.Account);
        catalogue.Models.Should().ContainSingle();
        catalogue.Models[0].Reasoning.Single().Id.Value.Should().Be("disabled");
        f.Requests.Should().ContainSingle().Which.Method.Should().Be("GET");
        f.SeenCredential.Should().Be(Fixture.Credential);
        f.SeenWorkspace.Should().Be(f.Lease.Workspace);
        var profile = f.Profile(catalogue);
        (await broker.IsProfileCurrentAsync(profile, Token)).Should().BeTrue();
        f.Bound = false;
        (await broker.IsProfileCurrentAsync(profile, Token)).Should().BeFalse();
        (await broker.ObserveAsync(Token)).Models.Should().BeEmpty();
        f.Audits.Select(item => item.ActionId).Should().NotContain(Fixture.Credential);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("null-limit")]
    [InlineData("retired")]
    [InlineData("thinking")]
    [InlineData("unknown-effort")]
    [InlineData("partial-page")]
    [InlineData("overflow")]
    [InlineData("duplicate-json")]
    public async Task Incomplete_unknown_and_oversized_catalogues_never_publish_partial_models(string fault)
    {
        var f = new Fixture();
        var model = Fixture.Model();
        f.ModelsJson = fault switch
        {
            "duplicate" => Fixture.Page([model, model]),
            "null-limit" => Fixture.Page([model.Replace("\"max_input_tokens\":8192", "\"max_input_tokens\":null", StringComparison.Ordinal)]),
            "retired" => Fixture.Page([model.Replace("\"active\"", "\"retired\"", StringComparison.Ordinal)]),
            "thinking" => Fixture.Page([model.Replace("\"disabled\":{\"supported\":true}", "\"disabled\":{\"supported\":false}", StringComparison.Ordinal)]),
            "unknown-effort" => Fixture.Page([model.Replace("\"supported\":false", "\"supported\":true,\"future\":{\"supported\":true}", StringComparison.Ordinal)]),
            "partial-page" => Fixture.Page([model], more: true, last: "foreign"),
            "overflow" => Fixture.Page(Enumerable.Range(0, 65).Select(index => Fixture.Model("model-" + index))),
            "duplicate-json" => "{\"data\":[],\"data\":[],\"has_more\":false}",
            _ => throw new InvalidOperationException(),
        };
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await Assert.ThrowsAnyAsync<Exception>(() => broker.SignInAsync(Fixture.Credential, () => true, Token));
        (await broker.ObserveAsync(Token)).Models.Should().BeEmpty();
        broker.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Pagination_is_complete_and_uses_only_exact_readonly_cursor_requests()
    {
        var f = new Fixture();
        f.Respond = request => Fixture.Json(request.RequestUri!.Query.Contains("after_id=", StringComparison.Ordinal)
            ? Fixture.Page([Fixture.Model("model-2")])
            : Fixture.Page([Fixture.Model("model-1")], true, "model-1"));
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(Fixture.Credential, () => true, Token);
        (await broker.ObserveAsync(Token)).Models.Select(model => model.Id.Value).Should().Equal("model-1", "model-2");
        f.Requests.Count.Should().Be(2);
        f.Requests.Should().OnlyContain(request => string.Equals(request.Method, "GET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_ignoring_setup_is_owned_until_real_http_completion_and_cannot_publish()
    {
        var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Send = (_, _) => { entered.TrySetResult(); return release.Task; };
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var setup = broker.SignInAsync(Fixture.Credential, () => true, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        await cancellation.CancelAsync();
        var disposal = broker.DisposeAsync().AsTask();
        broker.IsQuiescent.Should().BeFalse(); setup.IsCompleted.Should().BeFalse(); disposal.IsCompleted.Should().BeFalse();
        release.SetResult(Fixture.Json(f.ModelsJson));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup);
        await disposal;
        (await broker.ObserveAsync(Token)).Models.Should().BeEmpty();
        broker.IsQuiescent.Should().BeTrue();
    }

    internal sealed class Fixture : IClaudeDeploymentApprovalSource, ISecurityAuditLog
    {
        internal const string Credential = "synthetic-test-only-credential";
        internal TimeProvider Time { get; init; } = TimeProvider.System;
        internal bool Approved { get; set; } = true;
        internal bool Bound { get; set; } = true;
        internal Action? BeforeApprovalRead { get; set; }
        internal ClaudeDeploymentApproval Approval { get; init; } = new(DateTimeOffset.UtcNow.AddHours(1));
        internal ClaudeCredentialLease Lease { get; } = new(new("approved-opaque-account"), "wrkspc_test",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Credential))), DateTimeOffset.UtcNow.AddHours(1));
        internal List<(string Method, string Uri, byte[] Body)> Requests { get; } = [];
        internal List<SecurityAuditEvent> Audits { get; } = [];
        internal string? SeenCredential { get; private set; }
        internal string? SeenWorkspace { get; private set; }
        internal string ModelsJson { get; set; } = Page([Model()]);
        internal Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Send { get; set; }
        internal ClaudeAccountBroker Create() => new(this, Time, this, NullLogger<ClaudeAccountBroker>.Instance, CreateHttp);
        internal HttpClient CreateHttp() => new(new Handler(this));
        internal ProviderProfileTuple Profile(ProviderCapabilityCatalogue catalogue) => new(ProviderService.Claude,
            catalogue.Account!, catalogue.Revision, catalogue.Models[0].Id,
            catalogue.Models[0].Reasoning[0].Id, catalogue.Models[0].Context[0].Id);
        public ValueTask<ClaudeDeploymentApproval?> ResolveAsync(CancellationToken token)
        {
            BeforeApprovalRead?.Invoke();
            return ValueTask.FromResult(Approved ? Approval : null);
        }
        public ValueTask<ClaudeCredentialLease?> BindCredentialAsync(ClaudeDeploymentApproval approval, string credentialSha256, CancellationToken token) =>
            ValueTask.FromResult(Bound && ReferenceEquals(approval, Approval) && string.Equals(credentialSha256, Lease.CredentialSha256, StringComparison.Ordinal) ? Lease : null);
        public void Write(SecurityAuditEvent auditEvent) { HostActivity.RequireCurrent(); Audits.Add(auditEvent); }
        internal static string Model(string id = "exact-model") => "{\"type\":\"model\",\"id\":\"" + id
            + "\",\"display_name\":\"Exact test model\",\"lifecycle\":\"active\",\"max_input_tokens\":8192,\"max_tokens\":4096,"
            + "\"capabilities\":{\"thinking\":{\"types\":{\"disabled\":{\"supported\":true}}},\"effort\":{\"supported\":false}}}";
        internal static string Page(IEnumerable<string> models, bool more = false, string? last = null) =>
            "{\"data\":[" + string.Join(",", models) + "],\"has_more\":" + (more ? "true" : "false")
            + ",\"last_id\":" + JsonSerializer.Serialize(last) + "}";
        internal static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                fixture.SeenCredential = request.Headers.GetValues("x-api-key").Single();
                fixture.SeenWorkspace = request.Headers.GetValues("anthropic-workspace-id").Single();
                fixture.Requests.Add((request.Method.Method, request.RequestUri!.AbsoluteUri,
                    request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken)));
                return fixture.Send is not null ? await fixture.Send(request, cancellationToken)
                    : fixture.Respond?.Invoke(request) ?? Json(fixture.ModelsJson);
            }
        }
    }
}
