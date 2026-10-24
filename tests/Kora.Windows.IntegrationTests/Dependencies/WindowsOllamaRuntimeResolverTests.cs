using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

using System.Security.Cryptography;
using System.Text.Json;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class WindowsOllamaRuntimeResolverTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("qualified", true)]
    [InlineData("missing", false)]
    [InlineData("expired", false)]
    [InlineData("future", false)]
    [InlineData("revoked", false)]
    [InlineData("process-exited", false)]
    [InlineData("installation-unknown", false)]
    [InlineData("installation-missing", false)]
    [InlineData("replacement", false)]
    [InlineData("revoked-during-observation", false)]
    public async Task OnlyFreshExactQualifiedHostReceiptCanAdvertiseOrResolveAProfile(string state, bool available)
    {
        using var f = new Fixture();
        if (state is "missing") { f.Value = null; }
        if (state is "expired") { f.Time.Now = f.Time.Now.AddMinutes(2); }
        if (state is "future") { f.Time.Now = f.Time.Now.AddMinutes(-2); }
        if (state is "revoked") { await f.Revoked.CancelAsync(); }
        if (state is "process-exited") { f.ProcessCurrent = false; }
        if (state is "installation-unknown") { f.Installation = LocalProviderInstallation.Unknown; }
        if (state is "installation-missing") { f.Installation = LocalProviderInstallation.Missing; }
        if (state is "replacement") { f.OnObserve = () => f.Value = null; }
        if (state is "revoked-during-observation") { f.OnObserve = f.Revoked.Cancel; }
        var observation = await f.Resolver.ObserveAsync(Token);
        observation.For(ProviderService.Local).Status.Should().Be(available ? ProviderCatalogueStatus.Current : ProviderCatalogueStatus.Unavailable);
        observation.Catalogues.Where(item => item.Service != ProviderService.Local).Should()
            .OnlyContain(item => item.Status == ProviderCatalogueStatus.Unavailable && item.Models.Count == 0);
        (await f.Resolver.ResolveAsync(f.Selection, f.Profile, Token)).Should().Be(available ? f.Value : null);
        if (state is "missing" or "expired" or "future" or "revoked" or "installation-unknown" or "installation-missing")
        { f.Observations.Should().Be(0); }
    }

    [Theory]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("selection")]
    public async Task SelectionAndEveryTupleFieldAreExactRatherThanProviderSuppliedDefaults(string field)
    {
        using var f = new Fixture();
        var tuple = field switch
        {
            "account" => f.Profile with { Account = new("foreign") },
            "revision" => f.Profile with { CatalogueRevision = new(2) },
            "model" => f.Profile with { Model = new("hostile") },
            "reasoning" => f.Profile with { Reasoning = new("on") },
            "context" => f.Profile with { Context = new("unbounded") },
            _ => f.Profile,
        };
        var selection = field is "selection" ? f.Selection with { Model = new(Guid.NewGuid()) } : f.Selection;
        (await f.Resolver.ResolveAsync(selection, tuple, Token)).Should().BeNull();
    }

    [Fact]
    public async Task BindingIsFreshExactRevocableAndDoesNotSurviveAdmissionLoss()
    {
        using var f = new Fixture();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi), f.Selection);
        using var turn = new CancellationTokenSource();
        var admitted = true;
        var binding = await f.Resolver.BindAsync(f.Value!, provenance, f.Profile, () => admitted, turn.Token);
        binding.Should().NotBeNull();
        binding!.Provenance.Should().BeSameAs(provenance);
        binding.ProcessId.Should().Be(4242);
        binding.Model.Should().Be("deterministic-fake:one");
        binding.IsAdmitted().Should().BeTrue();
        admitted = false;
        binding.IsAdmitted().Should().BeFalse();
        (await f.Resolver.BindAsync(f.Value!, provenance, f.Profile, () => admitted, turn.Token)).Should().BeNull();
        admitted = true;
        await f.Revoked.CancelAsync();
        binding.IsAdmitted().Should().BeFalse();
        await turn.CancelAsync();
        binding.Revoked.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CancelledObservationDoesNotReturnACurrentCatalogue()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.OnObserve = cancellation.Cancel;
        await f.Resolver.Invoking(value => value.ObserveAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ActualDesktopSourceHasNoQualifiedProfileAndNeverObservesAProcess()
    {
        using var f = new Fixture();
        (await new Kora.Definitions.Qualifications.BundledLocalModelQualificationAuthority().Read(Token)).Should().BeEmpty();
        f.Value = null;
        var resolver = new WindowsOllamaRuntimeResolver(f, f, f.Time, NullLogger<WindowsOllamaRuntimeResolver>.Instance,
            (_, _) => throw new InvalidOperationException("No native process observation is authorized."));
        (await resolver.ObserveAsync(Token)).For(ProviderService.Local).Models.Should().BeEmpty();
    }

    [Theory]
    [InlineData("pid")]
    [InlineData("path")]
    [InlineData("unc")]
    [InlineData("hash")]
    [InlineData("start")]
    public void InvalidRuntimeIdentitiesAreRejected(string field)
    {
        var action = () => new WindowsOllamaRuntimeIdentity(field is "pid" ? 0 : 4242,
            field is "start" ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow,
            field is "path" ? "relative.exe" : field is "unc" ? @"\\host\share\ollama.exe" : @"C:\fake\ollama.exe",
            field is "hash" ? "unknown" : Digest);
        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("model-digest")]
    [InlineData("revocation")]
    [InlineData("unimplemented-reasoning")]
    [InlineData("unimplemented-context")]
    [InlineData("managed-reasoning")]
    [InlineData("selectable-context")]
    [InlineData("process-after-evidence")]
    public void IncompleteOrUnimplementedQualificationCannotInventAnExecutableProfile(string field)
    {
        using var f = new Fixture();
        var original = f.Value!;
        var profile = f.Profile;
        var model = new ProviderCatalogueModel(profile.Model, "Deterministic fake", f.Selection,
            [new(new(field is "unimplemented-reasoning" ? "on" : "off"), "Reasoning", field is "managed-reasoning")],
            [new(new(field is "unimplemented-context" ? "unbounded" : "bounded"), "Context", field is not "selectable-context")]);
        var catalogue = new ProviderCapabilityCatalogue(ProviderService.Local, ProviderCatalogueStatus.Current,
            profile.Account, profile.CatalogueRevision, f.Time.Now, f.Time.Now.AddMinutes(1), [model]);
        var action = () => new WindowsOllamaQualification(field is "evidence" ? Guid.Empty : Guid.NewGuid(), catalogue,
            field is "process-after-evidence"
                ? new(4242, f.Time.Now.AddSeconds(1), @"C:\fake\ollama.exe", Digest) : original.Runtime,
            original.RuntimeVersion, original.Model, field is "model-digest" ? "unknown" : Digest,
            field is "revocation" ? CancellationToken.None : f.Revoked.Token);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IPv6ObservationMustBeKnownAndCannotHideAnAdditionalRuntimeListener()
    {
        WindowsOllamaToolProcessObservation.HasNoIpv6Listener([]).Should().BeFalse();
        WindowsOllamaToolProcessObservation.HasNoIpv6Listener([1, 0, 0, 0]).Should().BeFalse();
        WindowsOllamaToolProcessObservation.HasNoIpv6Listener([0, 0, 0, 0]).Should().BeTrue();
        var table = new byte[60];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(24), 11434);
        WindowsOllamaToolProcessObservation.HasNoIpv6Listener(table).Should().BeFalse();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(24), 11435);
        WindowsOllamaToolProcessObservation.HasNoIpv6Listener(table).Should().BeTrue();
    }

    internal sealed class Fixture : IWindowsOllamaQualificationSource, ILocalProviderInstallationObservation, IDisposable
    {
        internal Clock Time { get; } = new();
        internal CancellationTokenSource Revoked { get; } = new();
        internal WindowsOllamaQualification? Value;
        internal LocalProviderInstallation Installation = LocalProviderInstallation.Installed;
        internal bool ProcessCurrent = true;
        internal int Observations;
        internal Action? OnObserve;
        internal ModelProviderSelection Selection { get; } = ModelProviderSelection.OllamaCandidate;
        internal ProviderProfileTuple Profile { get; } = new(ProviderService.Local, new("fake-host-account"), new(1),
            new("fake-model"), new("off"), new("bounded"));
        internal WindowsOllamaRuntimeResolver Resolver { get; }
        private static readonly string[] Gates = ["licence-identity", "quality-authority", "bounds", "cancellation-recovery",
            "resource-latency", "offline-egress", "integrated-host"];
        internal Fixture()
        {
            var model = new ProviderCatalogueModel(Profile.Model, "Deterministic fake", Selection,
                [new(Profile.Reasoning, "Off", false)], [new(Profile.Context, "Host bounded", true)]);
            var catalogue = new ProviderCapabilityCatalogue(ProviderService.Local, ProviderCatalogueStatus.Current,
                Profile.Account, Profile.CatalogueRevision, Time.Now, Time.Now.AddMinutes(1), [model]);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Format = 1,
                HostContract = LocalModelQualificationEvidence.HostContract,
                ProfileId = Selection.Model.Value,
                Revision = 1,
                Label = "Deterministic fixture",
                RuntimeVersion = "fake-runtime",
                ExecutableSha256 = Digest,
                Model = "deterministic-fake:one",
                ModelSha256 = Digest,
                Reasoning = "off",
                Context = "bounded",
                IssuedAt = Time.Now,
                ExpiresAt = Time.Now.AddMinutes(1),
                Proofs = Gates.Select(gate => new
                {
                    Gate = gate,
                    SourceRevision = new string('b', 40),
                    RunId = "fixture-only",
                    ArtifactSha256 = Digest,
                    Outcome = "passed"
                }).ToArray(),
            });
            var qualification = LocalModelQualificationEvidence.Read(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
            Value = new(Guid.NewGuid(), catalogue,
                new(4242, Time.Now, @"C:\fake\ollama.exe", Digest), "fake-runtime", "deterministic-fake:one", Digest, Revoked.Token, qualification);
            Resolver = new(this, this, Time, NullLogger<WindowsOllamaRuntimeResolver>.Instance, (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                Observations++;
                OnObserve?.Invoke();
                return Task.FromResult(ProcessCurrent);
            });
        }
        public Task<WindowsOllamaQualification?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Value);
        }
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Installation);
        }
        public void Dispose() => Revoked.Dispose();
    }
    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}