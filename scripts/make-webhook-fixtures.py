#!/usr/bin/env python3
"""Writes the synthetic Jira webhook fixtures under tests/.../Fixtures/webhooks/.

Why synthetic: a live capture needs a public tunnel to a local site and a Jira admin account,
which the plan's Task 4 step 5 leaves to Pascal (docs/spikes.md, Task 4). The shapes follow
Atlassian's published webhook examples
(https://developer.atlassian.com/cloud/jira/platform/webhooks/, read 2026-11-04). Every
person, email and account ID is invented and listed in synthetic-allowlist.txt.

issue_updated.signed.json is self-signed with a fixture-only secret. It proves the encoding
path, not compatibility with a real Jira delivery; replace it with a scrubbed captured delivery
when the tunnel test is done.

Run: python3 scripts/make-webhook-fixtures.py
"""
import hashlib
import hmac
import json
import pathlib

root = pathlib.Path(__file__).resolve().parent.parent / "tests" / "Umbraco.Community.Automate.Jira.Tests" / "Fixtures"
out = root / "webhooks"
out.mkdir(parents=True, exist_ok=True)

SITE = "https://example-sandbox.atlassian.net"
ANN = {"self": f"{SITE}/rest/api/2/user?accountId=000000:00000000-0000-4000-8000-000000000001",
       "accountId": "000000:00000000-0000-4000-8000-000000000001", "displayName": "Ann Example",
       "active": True, "timeZone": "Europe/London", "accountType": "atlassian"}
BOB = {"self": f"{SITE}/rest/api/2/user?accountId=000000:00000000-0000-4000-8000-000000000002",
       "accountId": "000000:00000000-0000-4000-8000-000000000002", "displayName": "Bob Example",
       "active": True, "timeZone": "Europe/London", "accountType": "atlassian"}


def issue(key="SBX-17", summary="Publish the spring campaign page", status="To Do"):
    return {
        "id": "10017", "self": f"{SITE}/rest/api/2/10017", "key": key,
        "fields": {
            "summary": summary,
            "status": {"name": status, "id": "10000", "statusCategory": {"key": "new", "name": "To Do"}},
            "issuetype": {"id": "10001", "name": "Task"},
            "project": {"id": "10000", "key": "SBX", "name": "Sandbox"},
            "reporter": ANN, "assignee": BOB,
            "labels": ["web"], "created": "2026-11-04T09:00:00.000+0000", "updated": "2026-11-04T09:30:00.000+0000",
        },
    }


def comment(cid="10050", body="Copy approved by marketing.", author=BOB):
    return {"self": f"{SITE}/rest/api/2/issue/10017/comment/{cid}", "id": cid, "author": author,
            "body": body, "updateAuthor": author, "created": "2026-11-04T09:40:00.000+0000",
            "updated": "2026-11-04T09:40:00.000+0000", "jsdPublic": True}


fixtures = {
    "issue_created.json": {
        "timestamp": 1762246800000, "webhookEvent": "jira:issue_created", "issue_event_type_name": "issue_created",
        "user": ANN, "issue": issue()},
    "issue_updated_status.json": {
        "timestamp": 1762248600000, "webhookEvent": "jira:issue_updated", "issue_event_type_name": "issue_generic",
        "user": BOB, "issue": issue(status="Ready to publish"),
        "changelog": {"id": "10200", "items": [
            {"field": "status", "fieldtype": "jira", "fieldId": "status", "from": "10000", "fromString": "To Do",
             "to": "10003", "toString": "Ready to publish"}]}},
    "issue_updated_comment_only.json": {
        "timestamp": 1762249200000, "webhookEvent": "jira:issue_updated", "issue_event_type_name": "issue_commented",
        "user": BOB, "issue": issue(status="Ready to publish"), "comment": comment()},
    "comment_created.json": {
        "timestamp": 1762249200000, "webhookEvent": "comment_created", "comment": comment(),
        "issue": {"id": "10017", "self": f"{SITE}/rest/api/2/10017", "key": "SBX-17",
                  "fields": {"summary": "Publish the spring campaign page", "issuetype": {"id": "10001", "name": "Task"},
                             "project": {"id": "10000", "key": "SBX", "name": "Sandbox"}}}},
    "comment_updated.json": {
        "timestamp": 1762249500000, "webhookEvent": "comment_updated",
        "comment": comment(body="Copy approved by marketing. Hero image swapped."),
        "issue": {"id": "10017", "self": f"{SITE}/rest/api/2/10017", "key": "SBX-17",
                  "fields": {"summary": "Publish the spring campaign page", "issuetype": {"id": "10001", "name": "Task"},
                             "project": {"id": "10000", "key": "SBX", "name": "Sandbox"}}}},
}

for name, payload in fixtures.items():
    (out / name).write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

# Fixture-only secret (36 characters, above the 32-character minimum); never used anywhere else.
secret = "fixture-only-webhook-secret-00000000"
body = json.dumps({**fixtures["issue_updated_status.json"],
                   "issue": issue(summary="Publish the café page ☕ for spring", status="Ready to publish")},
                  separators=(",", ":"), ensure_ascii=False)
signature = "sha256=" + hmac.new(secret.encode(), body.encode("utf-8"), hashlib.sha256).hexdigest()
signed = {
    "source": "synthetic, self-signed with a fixture-only secret; not a captured Jira delivery",
    "secret": secret,
    "headers": {"X-Hub-Signature": signature,
                "X-Atlassian-Webhook-Identifier": "00000000-0000-4000-8000-0000000000aa"},
    "timestamp": json.loads(body)["timestamp"],
    "body": body,
}
(out / "issue_updated.signed.json").write_text(json.dumps(signed, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

(root / "synthetic-allowlist.txt").write_text(
    "# Invented personal data allowed in fixtures (FixtureScannerTests). One value per line.\n"
    "Ann Example\nBob Example\n"
    "000000:00000000-0000-4000-8000-000000000001\n000000:00000000-0000-4000-8000-000000000002\n"
    "ann.example@example.invalid\nbob.example@example.invalid\nexample-sandbox.atlassian.net\n", encoding="utf-8")
print("wrote", len(fixtures) + 1, "webhook fixtures")
