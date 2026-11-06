using System.Text.Json.Nodes;

namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>Turns text into an ADF document that has been through <c>AdfSanitiser</c>.</summary>
public interface IAdfConverter
{
    /// <summary>Converts <paramref name="input"/>. Blank input gives a null document, so no field is sent.</summary>
    AdfResult Convert(string? input);
}

/// <summary>A converted document and what the safety pass changed, for the step's run log.</summary>
/// <param name="Document">The ADF <c>doc</c>, or null for empty or whitespace input.</param>
/// <param name="Warnings">One line per kind of content removed or cut. Never contains the removed content.</param>
public sealed record AdfResult(JsonObject? Document, IReadOnlyList<string> Warnings);

/// <summary>Picks the converter for a <see cref="JiraTextFormat"/>.</summary>
public interface IAdfConverterFactory
{
    /// <summary>The converter for <paramref name="format"/>.</summary>
    IAdfConverter For(JiraTextFormat format);
}
