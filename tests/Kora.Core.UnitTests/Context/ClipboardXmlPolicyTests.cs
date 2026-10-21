using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class ClipboardXmlPolicyTests
{
    [Theory]
    [InlineData("<root/>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-16\"?><root/>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?><r>🙂é</r>")]
    [InlineData("<!--before--><r xmlns=\"urn:one\" xmlns:p=\"urn:two\"><p:e a=\"v\"/>mixed<![CDATA[<not-an-element>]]>&amp;&#x1F642;</r><!--after-->")]
    [InlineData("<r>e\u0301\r\né\t🙂</r>")]
    [InlineData("<?xml-stylesheet href=\"https://must-not-load.invalid/a.xsl\"?><r/>")]
    [InlineData("<r xmlns:xi=\"http://www.w3.org/2001/XInclude\"><xi:include href=\"file:///must-not-read\"/></r>")]
    [InlineData("<r xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"https://must-not-load.invalid/a.xsd\"/>")]
    [InlineData("<r><same/><same/></r>")]
    public void CompleteDocumentIsCheckedWithoutRewritingOrExternalInterpretation(string text)
    {
        var snapshot = Snapshot(text);
        var result = ClipboardXmlPolicy.Validate(snapshot, TestContext.Current.CancellationToken);
        result.Should().Be(new ClipboardXmlValidationResult(snapshot, ClipboardXmlOutcome.WellFormedXml, ClipboardXmlReason.None, 0, 0));
        result.Source.Should().BeSameAs(snapshot);
        snapshot.Text.Should().Be(text);
    }

    [Theory]
    [InlineData(" \t\r\n")]
    [InlineData("<!--no root-->")]
    [InlineData("<?xml version=\"1.0\"?>")]
    [InlineData("<one/><two/>")]
    [InlineData("<root/>trailing")]
    [InlineData("<secret>\n<private></secret>")]
    [InlineData("<r>\u0001</r>")]
    [InlineData("<r a=\"one\" a=\"two\"/>")]
    [InlineData("<!DOCTYPE r [<!ENTITY secret 'expanded'>]><r>&secret;</r>")]
    [InlineData("<!DOCTYPE r SYSTEM 'file:///must-not-read'><r/>")]
    [InlineData("<!DOCTYPE r SYSTEM 'https://must-not-fetch.invalid/entity'><r/>")]
    [InlineData("<!DOCTYPE r [<!ENTITY % p SYSTEM 'https://must-not-fetch.invalid/parameter'>%p;]><r/>")]
    [InlineData("<!DOCTYPE r [<!ENTITY a '0123456789'><!ENTITY b '&a;&a;&a;&a;&a;&a;&a;&a;'>]><r>&b;</r>")]
    [InlineData("<r>&unknown;</r>")]
    public void MalformedOrProhibitedInputProducesOnlyTypedReasonAndPosition(string text)
    {
        var snapshot = Snapshot(text);
        var result = ClipboardXmlPolicy.Validate(snapshot, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(ClipboardXmlOutcome.Invalid);
        result.Reason.Should().Be(ClipboardXmlReason.MalformedOrProhibitedXml);
        result.Line.Should().BeGreaterThanOrEqualTo(0);
        result.Column.Should().BeGreaterThanOrEqualTo(0);
        result.Source.Should().BeSameAs(snapshot);
        // No raw exception, name, token, excerpt or URI is carried by the result contract.
        typeof(ClipboardXmlValidationResult).GetProperties().Should().NotContain(property => property.PropertyType == typeof(string));
    }

    [Fact]
    public void InvalidPositionUsesParserLineAndColumnWithoutExceptionMessage()
    {
        var result = ClipboardXmlPolicy.Validate(Snapshot("<r>\n<x></r>"), TestContext.Current.CancellationToken);
        result.Line.Should().Be(2);
        result.Column.Should().Be(6);
    }

    [Theory]
    [InlineData(64, ClipboardXmlOutcome.WellFormedXml, ClipboardXmlReason.None)]
    [InlineData(65, ClipboardXmlOutcome.PolicyRefused, ClipboardXmlReason.ElementDepthExceeded)]
    public void ElementDepthCountsRootAsOne(int depth, ClipboardXmlOutcome outcome, ClipboardXmlReason reason)
    {
        var text = string.Concat(Enumerable.Repeat("<e>", depth)) + "text"
            + string.Concat(Enumerable.Repeat("</e>", depth));
        var result = ClipboardXmlPolicy.Validate(Snapshot(text), TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(outcome);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public void FullAdmittedByteBoundaryIsParsedIncludingTheLastCharacter()
    {
        var body = new string('a', ClipboardSnapshot.MaximumUtf8Bytes - 7);
        var snapshot = Snapshot("<r>" + body + "</r>");
        snapshot.Utf8Bytes.Should().Be(ClipboardSnapshot.MaximumUtf8Bytes);
        ClipboardXmlPolicy.Validate(snapshot, TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardXmlOutcome.WellFormedXml);
        var invalid = Snapshot("<r>" + body + "</x>");
        ClipboardXmlPolicy.Validate(invalid, TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardXmlOutcome.Invalid);
        var unicode = Snapshot("<r>" + new string('é', (ClipboardSnapshot.MaximumUtf8Bytes - 8) / 2) + "a</r>");
        unicode.Utf8Bytes.Should().Be(ClipboardSnapshot.MaximumUtf8Bytes);
        ClipboardXmlPolicy.Validate(unicode, TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardXmlOutcome.WellFormedXml);
        var oversize = () => Snapshot(snapshot.Text + " ");
        oversize.Should().Throw<InvalidDataException>();
        var empty = () => Snapshot("");
        empty.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NullAndCancellationDoNotClaimInvalidXml()
    {
        var missing = () => ClipboardXmlPolicy.Validate(null!, CancellationToken.None);
        missing.Should().Throw<ArgumentNullException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => ClipboardXmlPolicy.Validate(Snapshot("<r/>"), cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
    }

    private static ClipboardSnapshot Snapshot(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
}
