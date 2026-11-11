using System.Security.Cryptography;
using System.Text;

namespace Umbraco.Community.Automate.Jira.Webhooks;

/// <summary>
/// Parses and checks Jira's <c>X-Hub-Signature</c> (HMAC-SHA256 over the raw body, design 6.5).
/// </summary>
/// <remarks>
/// The signature is compared only with <see cref="CryptographicOperations.FixedTimeEquals"/>. A banned-API
/// analyser (BannedSymbols.txt) and an IL scan (<c>ArchitectureTests</c>) keep it that way (plan P5).
/// </remarks>
internal static class JiraSignature
{
    private const string Prefix = "sha256=";
    private const int HexLength = 64;

    /// <summary>
    /// Accepts exactly <c>sha256=</c> (ordinal, lower case) followed by exactly 64 hex characters of either case,
    /// with nothing trimmed. Anything else, including another method, is rejected.
    /// </summary>
    public static bool TryParseHeader(string? headerValue, out byte[]? signature)
    {
        signature = null;
        if (headerValue is null
            || headerValue.Length != Prefix.Length + HexLength
            || !headerValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var hex = headerValue.AsSpan(Prefix.Length);
        foreach (var c in hex)
        {
            // Char.IsAsciiHexDigit, not Convert's own check, so a non-ASCII digit can never get through.
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        signature = Convert.FromHexString(hex);
        return true;
    }

    /// <summary>HMAC-SHA256 of <paramref name="body"/> keyed with the UTF-8 secret, compared in fixed time.</summary>
    public static bool Verify(ReadOnlySpan<byte> body, string secret, ReadOnlySpan<byte> signature)
    {
        Span<byte> expected = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body, expected);

        // FixedTimeEquals returns false for different lengths without comparing.
        return CryptographicOperations.FixedTimeEquals(expected, signature);
    }
}
