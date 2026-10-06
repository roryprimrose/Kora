using AwesomeAssertions;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class OllamaModelIdentityTests
{
    [Theory]
    [InlineData("8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7")]
    [InlineData("8F68893C685C3DDFF2AA3FFFCE2AA60A30BB2DA65CA488B61FFF134A4D1730E7")]
    [InlineData("sha256:8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7")]
    [InlineData("SHA256:8F68893C685C3DDFF2AA3FFFCE2AA60A30BB2DA65CA488B61FFF134A4D1730E7")]
    public void AcceptsExactlyThePinnedHashInApiOrQualifiedForm(string digest)
    {
        OllamaModelIdentity.HasPinnedDigest(digest).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:")]
    [InlineData("sha256:changed")]
    [InlineData("sha512:8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7")]
    [InlineData("sha256:sha256:8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7")]
    [InlineData(" 8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7")]
    [InlineData("8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7 ")]
    [InlineData("8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730eG")]
    [InlineData("8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e8")]
    public void RejectsMissingMalformedAndDifferentHashes(string? digest)
    {
        OllamaModelIdentity.HasPinnedDigest(digest).Should().BeFalse();
    }
}
