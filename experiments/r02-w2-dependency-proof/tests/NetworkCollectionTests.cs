using AwesomeAssertions;
using ContainmentProof;
using System.Text;
using System.Xml;

namespace W2Proof.Tests;

public sealed class NetworkCollectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private const string TemporaryRoot = @"C:\synthetic-temp";

    [Fact]
    public void FixedSyntheticRequestValidates() =>
        NetworkCollection.Validate(Request(), TemporaryRoot, Now);

    [Theory]
    [InlineData("other")]
    [InlineData("appcontainer-lost-receipt")]
    [InlineData("appcontainer-malformed-receipt")]
    public void RejectsUnpreparedProfiles(string profile)
    {
        Action operation = () => NetworkCollection.Validate(Request() with { Profile = profile }, TemporaryRoot, Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(@"C:\synthetic-temp\kora.r02.bad")]
    [InlineData(@"C:\other-temp\kora.r02.11111111111111111111111111111111")]
    [InlineData(@"C:\synthetic-temp\child\kora.r02.11111111111111111111111111111111")]
    [InlineData(@"C:\synthetic-temp\alias\..\kora.r02.11111111111111111111111111111111")]
    public void RejectsAlternateScratchPaths(string path)
    {
        Action operation = () => NetworkCollection.Validate(Request() with { Scratch = path }, TemporaryRoot, Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void RejectsInvalidPorts(int port)
    {
        Action operation = () => NetworkCollection.Validate(Request() with { Port = port }, TemporaryRoot, Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("host-name")]
    public void RejectsNonOwnedEndpointShapes(string address)
    {
        Action operation = () => NetworkCollection.Validate(Request() with { Address = address }, TemporaryRoot, Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsBroadenedImageScope()
    {
        var request = Request();
        request.Images[0] = request.Images[0] with { Path = @"C:\Windows\System32\svchost.exe" };
        Action operation = () => NetworkCollection.Validate(request, TemporaryRoot, Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsContainerIdentityOnUncontainedControl()
    {
        Action operation = () => NetworkCollection.Validate(Request() with { Profile = "job-only-complete" },
            TemporaryRoot, Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(120)]
    [InlineData(121)]
    public void RejectsFutureOrExpiredWindows(int age)
    {
        var request = Request() with { StartedUtc = Now.AddSeconds(-age), EndedUtc = Now.AddSeconds(-age) };
        Action operation = () => NetworkCollection.Validate(request, TemporaryRoot, Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsReversedWindow()
    {
        Action operation = () => NetworkCollection.Validate(
            Request() with { EndedUtc = Now.AddSeconds(-30) }, TemporaryRoot, Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void QueryContainsEveryRequiredFilter()
    {
        var request = Request();
        var arguments = NetworkCollection.QueryArguments(request, request.Images[0], request.Address,
            @"C:\synthetic-output\events.xml", Now);
        arguments.Should().Equal("wfp", "show", "netevents", @"file=C:\synthetic-output\events.xml",
            "protocol=6", "remoteaddr=192.0.2.10", "remoteport=32123",
            $"appid={request.Images[0].Path}", "userid=S-1-5-21-123", "timewindow=20");
        arguments.Should().NotContain("capture").And.NotContain("set");
    }

    [Fact]
    public void QueryRejectsDifferentEndpoint()
    {
        var request = Request();
        Action operation = () => NetworkCollection.QueryArguments(request, request.Images[0], "192.0.2.11",
            @"C:\synthetic-output\events.xml", Now);
        operation.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("collect-network", "consent-filtered-buffered-events", "Strict")]
    [InlineData("collect-network-user-comparison", "consent-application-endpoint-events-without-user-filter",
        "UserFilterComparison")]
    public void ExactConsentSelectsOnlyItsMode(string command, string consent, string expected) =>
        NetworkCollection.ObserverMode(command, consent).ToString().Should().Be(expected);

    [Theory]
    [InlineData("collect-network-user-comparison", "consent-filtered-buffered-events")]
    [InlineData("collect-network", "consent-application-endpoint-events-without-user-filter")]
    [InlineData("collect-network-user-comparison", "")]
    [InlineData("collect-network-user-comparison", "consent")]
    [InlineData("other", "consent-application-endpoint-events-without-user-filter")]
    public void RejectsMissingOrReusedConsent(string command, string consent)
    {
        Action operation = () => NetworkCollection.ObserverMode(command, consent);
        operation.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComparisonAddsOnlyOneDiagnosticArmWithoutChangingStrictMode()
    {
        NetworkCollection.QueryScopes(NetworkObserverMode.Strict).Should().Equal(NetworkQueryScope.Strict);
        NetworkCollection.QueryScopes(NetworkObserverMode.UserFilterComparison)
            .Should().Equal(NetworkQueryScope.Strict, NetworkQueryScope.ApplicationOnlyDiagnostic);
    }

    [Theory]
    [InlineData(0, "127.0.0.1")]
    [InlineData(0, "192.0.2.10")]
    [InlineData(1, "127.0.0.1")]
    [InlineData(1, "192.0.2.10")]
    public void DiagnosticRemovesOnlyUserFilterForEveryImageAndEndpoint(int image, string address)
    {
        var request = Request();
        const string destination = @"C:\synthetic-output\events.xml";
        var strict = NetworkCollection.QueryArguments(request, request.Images[image], address, destination, Now);
        var diagnostic = NetworkCollection.QueryArguments(request, request.Images[image], address, destination,
            Now, NetworkQueryScope.ApplicationOnlyDiagnostic);
        diagnostic.Should().Equal(strict.Where(argument => !argument.StartsWith("userid=", StringComparison.Ordinal)));
        diagnostic.Should().HaveCount(strict.Length - 1);
    }

    [Fact]
    public void DiagnosticStillRejectsDifferentImageEndpointAndExpiredWindow()
    {
        var request = Request();
        const string destination = @"C:\synthetic-output\events.xml";
        Action image = () => NetworkCollection.QueryArguments(request,
            request.Images[0] with { Path = @"C:\Windows\System32\svchost.exe" }, request.Address,
            destination, Now, NetworkQueryScope.ApplicationOnlyDiagnostic);
        Action endpoint = () => NetworkCollection.QueryArguments(request, request.Images[0], "192.0.2.11",
            destination, Now, NetworkQueryScope.ApplicationOnlyDiagnostic);
        Action expired = () => NetworkCollection.QueryArguments(request, request.Images[0], request.Address,
            destination, Now.AddSeconds(100), NetworkQueryScope.ApplicationOnlyDiagnostic);
        image.Should().Throw<InvalidDataException>();
        endpoint.Should().Throw<InvalidDataException>();
        expired.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsUnknownModesScopesAndNegativeElapsedTime()
    {
        var request = Request();
        Action mode = () => NetworkCollection.QueryScopes((NetworkObserverMode)123);
        Action scope = () => NetworkCollection.QueryArguments(request, request.Images[0], request.Address,
            @"C:\synthetic-output\events.xml", Now, (NetworkQueryScope)123);
        Action budget = () => NetworkCollection.QueryTimeout((NetworkObserverMode)123, TimeSpan.Zero);
        Action negative = () => NetworkCollection.QueryTimeout(NetworkObserverMode.UserFilterComparison,
            TimeSpan.FromTicks(-1));
        mode.Should().Throw<InvalidDataException>();
        scope.Should().Throw<InvalidDataException>();
        budget.Should().Throw<InvalidDataException>();
        negative.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(49, 10)]
    [InlineData(50, 10)]
    [InlineData(59, 1)]
    [InlineData(59.75, 0.25)]
    public void ComparisonCapsEachQueryByRemainingRowBudget(double elapsed, double limit) =>
        NetworkCollection.QueryTimeout(NetworkObserverMode.UserFilterComparison, TimeSpan.FromSeconds(elapsed))
            .Should().Be(TimeSpan.FromSeconds(limit));

    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    public void ExhaustedComparisonBudgetStopsWithoutAnotherQuery(int elapsed)
    {
        Action operation = () => NetworkCollection.QueryTimeout(NetworkObserverMode.UserFilterComparison,
            TimeSpan.FromSeconds(elapsed));
        operation.Should().Throw<TimeoutException>();
    }

    [Fact]
    public void StrictQueryKeepsOriginalTenSecondLimit() =>
        NetworkCollection.QueryTimeout(NetworkObserverMode.Strict, TimeSpan.FromSeconds(65))
            .Should().Be(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task MatchingReceiptAcknowledgesInspectionOnly()
    {
        await NetworkCollection.AwaitCompletionAsync("request", "digest",
            () => Task.FromResult<NetworkCompletion?>(new("request", "digest", "CollectedForInspection", "Unproven")),
            () => TimeSpan.Zero, () => throw new InvalidOperationException("No delay expected."));
    }

    [Theory]
    [InlineData("other", "digest", "CollectedForInspection", "Unproven")]
    [InlineData("request", "other", "CollectedForInspection", "Unproven")]
    [InlineData("request", "digest", "CollectedForInspection", "Denied")]
    [InlineData("request", "digest", "Failed", "Unproven")]
    public async Task RejectsMismatchedOrAuthorityClaimingReceipts(string id, string digest, string status,
        string attribution)
    {
        Func<Task> operation = () => NetworkCollection.AwaitCompletionAsync("request", "digest",
            () => Task.FromResult<NetworkCompletion?>(new(id, digest, status, attribution)), () => TimeSpan.Zero,
            () => throw new InvalidOperationException("No replay expected."));
        await operation.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task MissingReceiptTimesOutAtExactBoundWithoutReplay()
    {
        TimeSpan elapsed = TimeSpan.Zero;
        int reads = 0;
        Func<Task> operation = () => NetworkCollection.AwaitCompletionAsync("request", "digest", () =>
        {
            reads++;
            return Task.FromResult<NetworkCompletion?>(null);
        }, () => elapsed, () => { elapsed += TimeSpan.FromSeconds(30); return Task.CompletedTask; });
        await operation.Should().ThrowAsync<TimeoutException>();
        elapsed.Should().Be(TimeSpan.FromSeconds(90));
        reads.Should().Be(3);
    }

    [Fact]
    public async Task CancelledWaitPropagatesWithoutAnotherReadOrReplay()
    {
        int reads = 0;
        Func<Task> operation = () => NetworkCollection.AwaitCompletionAsync("request", "digest", () =>
        {
            reads++;
            return Task.FromResult<NetworkCompletion?>(null);
        }, () => TimeSpan.Zero, () => Task.FromCanceled(new CancellationToken(true)));
        await operation.Should().ThrowAsync<OperationCanceledException>();
        reads.Should().Be(1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(120)]
    public void QueryRejectsInvalidLookback(int seconds)
    {
        Action operation = () => NetworkCollection.TimeWindow(Request(), Now.AddSeconds(seconds - 20));
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FractionalLookbackRoundsUpWithinTheBound() =>
        NetworkCollection.TimeWindow(Request(), Now.AddSeconds(99.5)).Should().Be(120);

    [Fact]
    public void AcceptsXmlAtExactByteLimitForInspectionOnly()
    {
        string xml = "<events>" + new string(' ', NetworkCollection.MaximumXmlBytes - 17) + "</events>";
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(xml));
        NetworkCollection.InspectXml(input);
        input.Length.Should().Be(NetworkCollection.MaximumXmlBytes);
    }

    [Fact]
    public void RejectsXmlOneByteAboveLimit()
    {
        using var input = new MemoryStream(new byte[NetworkCollection.MaximumXmlBytes + 1]);
        Action operation = () => NetworkCollection.InspectXml(input);
        operation.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsExternalEntitiesWithoutReadingThem()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(
            "<!DOCTYPE events [<!ENTITY private SYSTEM 'file:///C:/synthetic-private.txt'>]><events>&private;</events>"));
        Action operation = () => NetworkCollection.InspectXml(input);
        operation.Should().Throw<XmlException>();
    }

    private static NetworkRequest Request()
    {
        const string scratch = TemporaryRoot + @"\kora.r02.11111111111111111111111111111111";
        return new("22222222222222222222222222222222", "appcontainer-complete", scratch,
            "S-1-5-21-123", "S-1-15-2-123", Now.AddSeconds(-20), Now.AddSeconds(-1), "192.0.2.10", 32123,
            [new("dotnet", scratch + @"\runtime\proof\ContainmentProof.exe", new string('A', 64)),
                new("powershell", scratch + @"\runtime\powershell\pwsh.exe", new string('B', 64))]);
    }
}
