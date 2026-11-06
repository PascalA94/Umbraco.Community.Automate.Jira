#!/usr/bin/env python3
"""Spike S4 and the dedupe probes (plan Task 4 steps 3 and 4), gateway route only.
Prints status codes, Jira error messages and timings; never the credential."""
import json
import secrets
import time

src = open(__file__.replace("spike_jira_s4.py", "spike_jira.py")).read().split("out = {}")[0]
g = {"__file__": __file__}
exec(src, g)
req, site, project, adf, errs = g["req"], g["site"], g["project"], g["adf"], g["errs"]
_, tenant, _ = req("GET", site + "/_edge/tenant_info", auth=False)
api = f"https://api.atlassian.com/ex/jira/{tenant['cloudId']}/rest/api/3"
out = {}


def create(fields_extra=None, properties=None, summary="S4 probe"):
    fields = {"project": {"key": project}, "issuetype": {"name": "Task"}, "summary": summary, "labels": ["cj-spike"]}
    fields.update(fields_extra or {})
    body = {"fields": fields}
    if properties is not None:
        body["properties"] = properties
    return req("POST", api + "/issue", body)


# S4: text cap.
for n in (32767, 32768, 40000):
    s, b, _ = create({"description": adf("x" * n)}, summary=f"S4 {n} chars")
    out[f"description_{n}"] = {"status": s, "key": b.get("key") if s == 201 else None, "error": None if s == 201 else errs(b)}

# S4: unsupported or risky ADF nodes.
risky = {"type": "doc", "version": 1, "content": [
    {"type": "paragraph", "content": [
        {"type": "text", "text": "a "},
        {"type": "inlineCard", "attrs": {"url": "https://example.invalid/card"}},
        {"type": "mention", "attrs": {"id": "000000000000000000000000", "text": "@Ann Example"}}]},
    {"type": "extension", "attrs": {"extensionType": "com.atlassian.confluence.macro.core", "extensionKey": "toc"}}]}
s, b, _ = create({"description": risky}, summary="S4 risky ADF")
out["risky_adf"] = {"status": s, "key": b.get("key") if s == 201 else None, "error": None if s == 201 else errs(b)}
if s == 201:
    s2, b2, _ = req("GET", f"{api}/issue/{b['key']}?fields=description")
    out["risky_adf"]["stored_node_types"] = [n.get("type") for n in (b2["fields"]["description"] or {}).get("content", [])] if s2 == 200 else s2


def poll(jql, timeout=90):
    start = time.monotonic()
    while time.monotonic() - start < timeout:
        s, b, _ = req("POST", api + "/search/jql", {"jql": jql, "maxResults": 1, "fields": ["key"]})
        if s != 200:
            return {"status": s, "error": errs(b)}
        if b.get("issues"):
            return {"matched_after_s": round(time.monotonic() - start, 1)}
        time.sleep(1)
    return {"matched_after_s": None, "timed_out_s": timeout}


# Probe (a): issue property, searchable by JQL?
marker = secrets.token_hex(8)
t0 = time.monotonic()
s, b, _ = create(properties=[{"key": "community.jira.dedupe", "value": {"key": marker}}], summary="dedupe probe a")
out["probe_a_create"] = {"status": s, "key": b.get("key") if s == 201 else None, "error": None if s == 201 else errs(b)}
if s == 201:
    out["probe_a_search"] = poll(f'project = "{project}" AND issue.property[community.jira.dedupe].key = "{marker}"')

# Probe (b): label, with search lag.
marker_b = secrets.token_hex(8)
s, b, _ = create({"labels": ["cj-spike", f"cj-dedupe-{marker_b}"]}, summary="dedupe probe b")
out["probe_b_create"] = {"status": s, "key": b.get("key") if s == 201 else None}
if s == 201:
    out["probe_b_search"] = poll(f'project = "{project}" AND labels = "cj-dedupe-{marker_b}"')
    issue_b = b["key"]
    # Probe (c): comment properties on create, read back with expand=properties.
    marker_c = secrets.token_hex(8)
    s, b, _ = req("POST", f"{api}/issue/{issue_b}/comment",
                  {"body": adf("dedupe probe c"), "properties": [{"key": "community.jira.dedupe", "value": {"key": marker_c}}]})
    out["probe_c_create"] = {"status": s, "error": None if s == 201 else errs(b)}
    s, b, _ = req("GET", f"{api}/issue/{issue_b}/comment?orderBy=-created&maxResults=50&expand=properties")
    found = False
    if s == 200:
        for c in b.get("comments", []):
            for p in c.get("properties", []) or []:
                if p.get("key") == "community.jira.dedupe" and p.get("value", {}).get("key") == marker_c:
                    found = True
    out["probe_c_readback"] = {"status": s, "property_returned": found}

# A second property-search sample for lag, if (a) matched.
if out.get("probe_a_search", {}).get("matched_after_s") is not None:
    lags = []
    for _ in range(3):
        m = secrets.token_hex(8)
        s, b, _ = create(properties=[{"key": "community.jira.dedupe", "value": {"key": m}}], summary="dedupe lag sample")
        if s == 201:
            lags.append(poll(f'project = "{project}" AND issue.property[community.jira.dedupe].key = "{m}"').get("matched_after_s"))
    out["probe_a_lag_samples_s"] = lags

print(json.dumps(out, indent=1))
