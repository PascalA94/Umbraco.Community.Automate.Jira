namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>
/// Markdown or ADF JSON input over <c>AdfLimits.MaxInputBytes</c> (F-11). Thrown before any parsing, and mapped to
/// a <c>Validation</c> failure by the actions. The message never contains the input.
/// </summary>
public sealed class AdfInputTooLargeException(string message) : Exception(message);
