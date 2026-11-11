using System.Security.Cryptography;
using System.Text;
using Umbraco.Community.Automate.Jira.Webhooks;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraSignatureTests
{
    private static readonly string Hex64 = new('a', 64);

    // Atlassian's own vector, copied from https://developer.atlassian.com/cloud/jira/platform/webhooks/
    // ("Testing the webhook payload validation", read 2026-11-11). The secret is under 32 characters, so this
    // calls the HMAC routine directly and bypasses the authenticator's length rule (design 6.5).
    [Fact]
    public void Verify_AtlassianVector_Passes()
    {
        const string header = "sha256=a4771c39fbe90f317c7824e83ddef3caae9cb3d976c214ace1f2937e133263c9";

        Assert.True(JiraSignature.TryParseHeader(header, out var signature));
        Assert.True(JiraSignature.Verify(Encoding.UTF8.GetBytes("Hello World!"), "It's a Secret to Everybody", signature));
    }

    [Fact]
    public void Verify_WrongSecret_Fails()
    {
        const string header = "sha256=a4771c39fbe90f317c7824e83ddef3caae9cb3d976c214ace1f2937e133263c9";

        Assert.True(JiraSignature.TryParseHeader(header, out var signature));
        Assert.False(JiraSignature.Verify(Encoding.UTF8.GetBytes("Hello World!"), "It's a Secret to Somebody", signature));
    }

    [Fact]
    public void Verify_WrongLengthSignature_Fails()
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("b"));

        Assert.False(JiraSignature.Verify(Encoding.UTF8.GetBytes("b"), "k", mac.AsSpan(0, 31)));
    }

    // Plan P5: exact ordinal prefix, exactly 64 hex characters, nothing trimmed.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=0123456789abcdef0123456789abcdef01234567")]
    [InlineData("SHA256=HEX64")]
    [InlineData("Sha256=HEX64")]
    [InlineData("sha256=HEX64 ")]
    [InlineData("sha256=HEX64\t")]
    [InlineData(" sha256=HEX64")]
    [InlineData("sha256= HEX64")]
    [InlineData("sha256=HEX63")]
    [InlineData("sha256=HEX65")]
    [InlineData("sha256=HEX63g")]
    [InlineData("sha256=HEX63é")]
    [InlineData("sha256=HEX64,sha256=HEX64")]
    [InlineData("HEX64")]
    public void TryParseHeader_Rejects(string? value)
    {
        Assert.False(JiraSignature.TryParseHeader(Expand(value), out var signature));
        Assert.Null(signature);
    }

    [Fact]
    public void TryParseHeader_Sha512_Rejected()
    {
        Assert.False(JiraSignature.TryParseHeader("sha512=" + new string('a', 128), out _));
    }

    [Theory]
    [InlineData("sha256=HEX64")]
    [InlineData("sha256=HEX64UPPER")]
    public void TryParseHeader_Variants_Accepted(string value)
    {
        // Hex case does not weaken the HMAC: both decode to the same 32 bytes.
        Assert.True(JiraSignature.TryParseHeader(Expand(value), out var signature));
        Assert.Equal(32, signature!.Length);
    }

    private static string? Expand(string? value) => value?
        .Replace("HEX64UPPER", "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", StringComparison.Ordinal)
        .Replace("HEX65", Hex64 + "a", StringComparison.Ordinal)
        .Replace("HEX64", Hex64, StringComparison.Ordinal)
        .Replace("HEX63", Hex64[..63], StringComparison.Ordinal);
}
