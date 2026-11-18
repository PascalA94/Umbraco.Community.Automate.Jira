namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Backoffice editor aliases that differ between Umbraco lines.</summary>
internal static class EditorAliases
{
    /// <summary>A dropdown that allows one choice.</summary>
    /// <remarks>
    /// On Umbraco 19 <c>Umb.PropertyEditorUi.Dropdown</c> is always multi-select and the single-choice editor is
    /// <c>Umb.PropertyEditorUi.Dropdown.Single</c>, which does not exist before 19. Attributes need a constant, so
    /// the line picks the value at compile time. Automate 19's own <c>HttpRequestSettings</c> uses the same alias.
    /// </remarks>
#if AUTOMATE_19
    public const string SingleDropdown = "Umb.PropertyEditorUi.Dropdown.Single";
#else
    public const string SingleDropdown = "Umb.PropertyEditorUi.Dropdown";
#endif
}
