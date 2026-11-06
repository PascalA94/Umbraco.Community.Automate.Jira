namespace Umbraco.Community.Automate.Jira.Client;

/// <summary>The account a credential acts as.</summary>
public sealed record JiraUser(string AccountId, string DisplayName);

/// <summary>An issue type available in a project.</summary>
public sealed record JiraIssueType(string Id, string Name);

/// <summary>An issue Jira has just created.</summary>
public sealed record JiraCreatedIssue(string Id, string Key);

/// <summary>A comment Jira has just created.</summary>
public sealed record JiraCreatedComment(string Id);

/// <summary>A workflow transition available on an issue.</summary>
public sealed record JiraTransition(string Id, string Name, string ToStatusName);
