#!/usr/bin/env python3
"""Spikes S3 and S4 (plan Task 4) against the sandbox Jira.

Reads the site, project and credential from the environment and never prints the
credential, request bodies with personal data, or the Authorization header. Prints status
codes and the specific non-personal fields each spike needs.

Env: JIRA_SANDBOX_SITE, JIRA_SANDBOX_PROJECT,
     Umbraco__Automate__Jira__Credentials__sandbox__Email / __ApiToken
"""
import base64
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

site = os.environ["JIRA_SANDBOX_SITE"].strip().rstrip("/")
if not site.startswith("https://"):
    site = "https://" + site
# The Basic credential goes only to an Atlassian Cloud site or the Atlassian gateway, never to a
# mistyped host, and never across a redirect (urllib would keep the Authorization header).
if not re.fullmatch(r"https://[a-z0-9-]+\.atlassian\.net", site):
    sys.exit("JIRA_SANDBOX_SITE must be https://<name>.atlassian.net")
project = os.environ["JIRA_SANDBOX_PROJECT"].strip()


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise urllib.error.HTTPError(req.full_url, code, "redirect refused", headers, fp)


_opener = urllib.request.build_opener(_NoRedirect)
_auth = "Basic " + base64.b64encode(
    (os.environ["Umbraco__Automate__Jira__Credentials__sandbox__Email"] + ":"
     + os.environ["Umbraco__Automate__Jira__Credentials__sandbox__ApiToken"]).encode()).decode()


def req(method, url, body=None, auth=True):
    r = urllib.request.Request(url, method=method, data=None if body is None else json.dumps(body).encode())
    r.add_header("Accept", "application/json")
    if body is not None:
        r.add_header("Content-Type", "application/json")
    if auth:
        if not (url.startswith(site + "/") or url.startswith("https://api.atlassian.com/ex/jira/")):
            raise ValueError("refusing to send the credential to an unexpected host")
        r.add_header("Authorization", _auth)
    try:
        with _opener.open(r, timeout=60) as resp:
            text = resp.read().decode()
            return resp.status, (json.loads(text) if text else None), dict(resp.headers)
    except urllib.error.HTTPError as e:
        text = e.read().decode("utf-8", "replace")
        try:
            parsed = json.loads(text)
        except ValueError:
            parsed = text[:200]
        return e.code, parsed, dict(e.headers)


def errs(body):
    if isinstance(body, dict):
        return {"errorMessages": body.get("errorMessages"), "errors": body.get("errors")}
    return body


def adf(text):
    return {"type": "doc", "version": 1, "content": [{"type": "paragraph", "content": [{"type": "text", "text": text}]}]}


out = {}
status, tenant, _ = req("GET", site + "/_edge/tenant_info", auth=False)
cloud_id = tenant.get("cloudId") if isinstance(tenant, dict) else None
out["tenant_info"] = {"status": status, "has_cloudId": bool(cloud_id)}
routes = {"site": site + "/rest/api/3"}
if cloud_id:
    routes["gateway"] = f"https://api.atlassian.com/ex/jira/{cloud_id}/rest/api/3"

for name, api in routes.items():
    r = {}
    s, b, _ = req("GET", api + "/myself")
    r["myself"] = s
    s, b, _ = req("GET", f"{api}/issue/createmeta/{project}/issuetypes")
    r["createmeta_issuetypes"] = s
    if s == 200:
        r["issue_types"] = [t.get("name") for t in b.get("issueTypes", b.get("values", []))]
    s, b, _ = req("POST", api + "/search/jql", {"jql": f'project = "{project}"', "maxResults": 1, "fields": ["key"]})
    r["search_jql"] = s
    s, b, _ = req("GET", api + "/mypermissions?permissions=ADMINISTER,ADMINISTER_PROJECTS,SYSTEM_ADMIN")
    r["mypermissions"] = s
    if s == 200:
        r["held"] = {k: v.get("havePermission") for k, v in b.get("permissions", {}).items()}
    else:
        r["mypermissions_error"] = errs(b)
    s, b, _ = req("POST", api + "/issue", {"fields": {"project": {"key": project}, "issuetype": {"name": "Task"},
                                                     "summary": f"S3 probe via {name}", "description": adf("probe"),
                                                     "labels": ["cj-spike"]}})
    r["create_issue"] = s
    key = b.get("key") if s == 201 else None
    if not key:
        r["create_error"] = errs(b)
    if key:
        r["issue"] = key
        s, b, _ = req("POST", f"{api}/issue/{key}/comment", {"body": adf("probe comment")})
        r["add_comment"] = s
        s, b, _ = req("GET", f"{api}/issue/{key}/comment?expand=properties")
        r["list_comments"] = s
        s, b, _ = req("GET", f"{api}/issue/{key}/transitions")
        r["get_transitions"] = s
        transitions = [(t["id"], t["name"], t["to"]["name"]) for t in b.get("transitions", [])] if s == 200 else []
        r["transitions"] = transitions
        target = next((t for t in transitions if t[2].lower() == "ready to publish"), None)
        if target:
            s, b, _ = req("POST", f"{api}/issue/{key}/transitions", {"transition": {"id": target[0]}})
            r["do_transition_ready_to_publish"] = s
    out[name] = r

print(json.dumps(out, indent=1))
json.dump({"cloud_id_present": bool(cloud_id)}, sys.stderr)
