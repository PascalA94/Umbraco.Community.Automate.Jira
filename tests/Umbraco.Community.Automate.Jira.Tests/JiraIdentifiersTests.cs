using Umbraco.Community.Automate.Jira.Connection;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraIdentifiersTests
{
    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("SUP-1/..")]
    [InlineData("SUP-1?x")]
    [InlineData("SUP-0")]
    [InlineData("-1")]
    [InlineData("SU\nP-1")]
    [InlineData("")]
    [InlineData(null)]
    public void IssueKey_DotSegments_Rejected(string? input)
    {
        Assert.False(JiraIdentifiers.TryIssueKeyOrId(input, out var value, out var error));
        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.False(JiraIdentifiers.TryIssueKey(input, out _, out _));
    }

    [Theory]
    [InlineData("sup-17", "SUP-17")]
    [InlineData("  SUP-17 ", "SUP-17")]
    [InlineData("10042", "10042")]
    public void IssueKeyOrId_Valid_Normalised(string input, string expected)
    {
        Assert.True(JiraIdentifiers.TryIssueKeyOrId(input, out var value, out var error));
        Assert.Equal(expected, value);
        Assert.Null(error);
    }

    [Fact]
    public void IssueKey_KeysOnly_NumericRejected()
    {
        Assert.False(JiraIdentifiers.TryIssueKey("10042", out _, out _));
        Assert.True(JiraIdentifiers.TryIssueKey("sup-17", out var value, out _));
        Assert.Equal("SUP-17", value);
    }

    [Theory]
    [InlineData("A/B")]
    [InlineData("1A")]
    [InlineData("..")]
    [InlineData("A")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("SU\nP")]
    public void ProjectKey_Invalid_Rejected(string? input)
    {
        Assert.False(JiraIdentifiers.TryProjectKeyOrId(input, out var value, out var error));
        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("SUP", "SUP")]
    [InlineData("sup", "SUP")]
    [InlineData("10000", "10000")]
    public void ProjectKey_Valid_Accepted(string input, string expected)
    {
        Assert.True(JiraIdentifiers.TryProjectKeyOrId(input, out var value, out _));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("10042", true)]
    [InlineData(" 10042 ", true)]
    [InlineData("1004a", false)]
    [InlineData("-1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("１２", false)]
    public void IsNumericId_OnlyAsciiDigits(string? input, bool expected)
    {
        Assert.Equal(expected, JiraIdentifiers.IsNumericId(input));
    }

    // The Kelvin sign case-folds to ASCII 'k' under IgnoreCase, so a key must be ASCII-checked first.
    [Fact]
    public void IssueKey_NonAscii_Rejected()
    {
        Assert.False(JiraIdentifiers.TryIssueKey("KEY-1", out _, out _));
    }

    [Fact]
    public void QuoteJql_EscapesQuoteAndBackslash()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", JiraIdentifiers.QuoteJql("a\"b\\c"));
    }

    [Fact]
    public void QuoteJql_EscapesLineBreaks()
    {
        Assert.Equal("\"a\\nb\\rc\\td\"", JiraIdentifiers.QuoteJql("a\nb\rc\td"));
    }
}
