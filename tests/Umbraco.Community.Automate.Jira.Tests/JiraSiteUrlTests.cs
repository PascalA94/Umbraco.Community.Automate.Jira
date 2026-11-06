using Umbraco.Community.Automate.Jira.Connection;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraSiteUrlTests
{
    [Theory]
    [InlineData("https://acme.atlassian.net")]
    [InlineData("https://acme.atlassian.net/")]
    [InlineData("HTTPS://ACME.Atlassian.net")]
    [InlineData("  https://acme.atlassian.net  ")]
    public void SiteUrl_Valid_NormalisedToCanonicalForm(string input)
    {
        var ok = JiraSiteUrl.TryNormalise(input, out var uri, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("https://acme.atlassian.net", uri!.GetLeftPart(UriPartial.Authority));
        Assert.Equal("/", uri.AbsolutePath);
    }

    [Theory]
    [InlineData("http://acme.atlassian.net")]
    [InlineData("https://acme.atlassian.net/jira")]
    [InlineData("https://evil.com/.atlassian.net")]
    [InlineData("https://user@acme.atlassian.net")]
    [InlineData("https://acme.atlassian.net.evil.com")]
    [InlineData("https://acme.atlassian.net:8443")]
    [InlineData("https://a_b.atlassian.net")]
    [InlineData("https://acme.atlassian.net/?x=1")]
    [InlineData("https://acme.atlassian.net/#f")]
    [InlineData("https://a.b.atlassian.net")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SiteUrl_Invalid_Rejected(string? input)
    {
        var ok = JiraSiteUrl.TryNormalise(input, out var uri, out var error);

        Assert.False(ok);
        Assert.Null(uri);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void SiteUrl_RejectionDoesNotEchoInput()
    {
        JiraSiteUrl.TryNormalise("https://evil.example/SENTINEL-IN-URL", out _, out var error);

        Assert.DoesNotContain("SENTINEL-IN-URL", error);
    }

    // \z, not $: $ also matches before a final newline, so the validator itself must reject it
    // even if a caller forgets to trim.
    [Fact]
    public void SiteUrl_TrailingNewlineWithoutTrim_Rejected()
    {
        var ok = JiraSiteUrl.TryValidateExact("https://acme.atlassian.net\n", out var uri, out _);

        Assert.False(ok);
        Assert.Null(uri);
    }

    [Fact]
    public void SiteUrl_ExactValid_Accepted()
    {
        Assert.True(JiraSiteUrl.TryValidateExact("https://acme.atlassian.net/", out var uri, out _));
        Assert.Equal("https://acme.atlassian.net", uri!.GetLeftPart(UriPartial.Authority));
    }

    // The Kelvin sign case-folds to ASCII 'k' under IgnoreCase, so non-ASCII must be refused up front.
    [Fact]
    public void SiteUrl_NonAscii_Rejected()
    {
        Assert.False(JiraSiteUrl.TryNormalise("https://acKe.atlassian.net", out _, out _));
    }

    [Fact]
    public void CloudId_GuidWithSurroundingSpaces_Accepted()
    {
        var guid = Guid.NewGuid();

        var ok = JiraSiteUrl.TryParseCloudId($"  {guid}  ", out var parsed, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(guid, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CloudId_Blank_AcceptedAsNull(string? input)
    {
        Assert.True(JiraSiteUrl.TryParseCloudId(input, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("abc")]
    [InlineData("{6f9619ff-8b86-d011-b42d-00c04fc964ff}")]
    [InlineData("6f9619ff8b86d011b42d00c04fc964ff")]
    public void CloudId_NotAGuid_Rejected(string input)
    {
        var ok = JiraSiteUrl.TryParseCloudId(input, out var parsed, out var error);

        Assert.False(ok);
        Assert.Null(parsed);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
