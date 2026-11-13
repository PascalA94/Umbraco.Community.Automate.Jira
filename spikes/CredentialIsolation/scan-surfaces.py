#!/usr/bin/env python3
"""Spike-only (Task 0 S7 step 7, council test 10): scan a running spike site's surfaces for a
sentinel credential value. Calls every GET in the Management and Automate Management OpenAPI
documents (path parameters filled with IDs seen in earlier responses), runs every health check
group, and greps the site's log files and SQLite database. Prints counts, never values.

Usage: scan-surfaces.py <https-base-url> <site-dir> <client-secret-file> <sentinel>
"""
import json
import pathlib
import re
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request

base, site_dir, secret_file, sentinel = sys.argv[1:5]
ctx = ssl.create_default_context()
ctx.check_hostname = False
ctx.verify_mode = ssl.CERT_NONE
GUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")


TOKEN = [None]


def call(method, path, token=None, data=None, retry=True):
    if token is not None and TOKEN[0]:
        token = TOKEN[0]
    status, text = _call(method, path, token, data)
    if status == 401 and token is not None and retry:
        TOKEN[0] = fresh_token()
        return call(method, path, TOKEN[0], data, retry=False)
    return status, text


def _call(method, path, token=None, data=None):
    req = urllib.request.Request(base + path, method=method, data=data)
    if token:
        req.add_header("Authorization", "Bearer " + token)
    if data is not None:
        req.add_header("Content-Type", "application/x-www-form-urlencoded")
    try:
        with urllib.request.urlopen(req, context=ctx, timeout=30) as r:
            return r.status, r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except Exception as e:  # noqa: BLE001 - spike scanner, report and continue
        return 0, type(e).__name__


secret = pathlib.Path(secret_file).read_text().strip()


def fresh_token():
    # Back-office access tokens are short-lived; a long scan re-authenticates per section.
    _, body = call("POST", "/umbraco/management/api/v1/security/back-office/token", data=urllib.parse.urlencode(
        {"grant_type": "client_credentials", "client_id": __import__("os").environ.get("SPIKE_CLIENT_ID", "umbraco-back-office-spike"), "client_secret": secret}).encode())
    return json.loads(body)["access_token"]


token = TOKEN[0] = fresh_token()

import os, collections
EXTRA = [m for m in os.environ.get("SPIKE_EXTRA_MARKERS", "").split(",") if m]
extra_hits = collections.defaultdict(list)
log_entries_seen = [0, 0, 0]  # run details, step runs, log entries
hits, calls, statuses = [], 0, {}
skipped = set()
seen_ids = set()


def record(label, status, text):
    global calls
    calls += 1
    statuses[status] = statuses.get(status, 0) + 1
    seen_ids.update(GUID.findall(text.lower()))
    if sentinel in text:
        hits.append(label)
    for m in EXTRA:
        if m in text:
            extra_hits[m].append(label)


for doc in ("management", "automate-management"):
    # v18 serves /umbraco/openapi/{doc}.json; v17 serves Swashbuckle's /umbraco/swagger/{doc}/swagger.json.
    status, spec_text = call("GET", f"/umbraco/openapi/{doc}.json")
    if status != 200:
        status, spec_text = call("GET", f"/umbraco/swagger/{doc}/swagger.json")
    if status != 200:
        print(f"{doc}: openapi status {status}")
        continue
    spec = json.loads(spec_text)
    gets = [p for p, ops in spec["paths"].items() if "get" in ops]
    plain = [p for p in gets if "{" not in p]
    templated = [p for p in gets if "{" in p]
    token = fresh_token()
    for p in plain:
        record(f"GET {p}", *call("GET", p, token))
    # Two passes so IDs found by templated calls feed the next pass.
    for _ in range(2):
        token = fresh_token()
        for p in templated:
            names = re.findall(r"{([^}]+)}", p)
            if len(names) != 1:
                skipped.add(p)
                continue
            for value in sorted(seen_ids):
                status, text = call("GET", p.replace("{" + names[0] + "}", value), token)
                if status == 200:
                    record(f"GET {p} ({names[0]}=...)", status, text)
    print(f"{doc}: {len(plain)} plain GETs, {len(templated)} templated GETs scanned")

# Routes with two or more path parameters, filled from the parent resource (found missing by the
# positive control: stored step input is only reachable through step-run data).
A = "/umbraco/automate/management/api/v1"
for run_id in sorted(seen_ids):
    status, run_text = call("GET", f"{A}/runs/{run_id}", token)
    if status != 200:
        continue
    record(f"GET runs/{{id}}", status, run_text)
    try:
        rj = json.loads(run_text)
        log_entries_seen[0] += 1
        for sr in rj.get("stepRuns", []) or []:
            log_entries_seen[1] += 1
            log_entries_seen[2] += len(sr.get("logEntries", []) or [])
    except Exception:  # noqa: BLE001
        pass
    for step_run_id in set(GUID.findall(run_text.lower())) - {run_id}:
        status, text = call("GET", f"{A}/runs/{run_id}/step-runs/{step_run_id}/data", token)
        if status == 200:
            record("GET runs/{id}/step-runs/{stepRunId}/data", status, text)
    status, text = call("GET", f"{A}/runs/{run_id}/trigger-data", token)
    if status == 200:
        record("GET runs/{id}/trigger-data", status, text)
for entity_id in sorted(seen_ids):
    for entity_type in ("automation", "workspace", "connection"):
        status, text = call("GET", f"{A}/version-history/{entity_type}/{entity_id}", token)
        if status != 200:
            continue
        record("GET version-history/{entityType}/{entityId}", status, text)
        for version in (1, 2, 3):
            status, text = call("GET", f"{A}/version-history/{entity_type}/{entity_id}/{version}", token)
            if status == 200:
                record("GET version-history/{entityType}/{entityId}/{entityVersion}", status, text)

# Health checks: run every group.
token = fresh_token()
status, groups_text = call("GET", "/umbraco/management/api/v1/health-check-group?take=100", token)
groups = [g["name"] for g in json.loads(groups_text).get("items", [])] if status == 200 else []
for g in groups:
    record(f"POST health-check-group/{g}/check", *call("POST", f"/umbraco/management/api/v1/health-check-group/{urllib.parse.quote(g)}/check", token, data=b""))
print(f"health check groups run: {len(groups)}")

# Log viewer API plus raw log files and the SQLite database on disk.
record("GET log-viewer/log", *call("GET", "/umbraco/management/api/v1/log-viewer/log?take=1000", token))
disk_hits = []
extra_disk = collections.defaultdict(list)
for f in pathlib.Path(site_dir, "umbraco").rglob("*"):
    if f.is_file() and (f.suffix in (".json", ".txt", ".log", ".clef") or ".db" in f.name) and sentinel.encode() in f.read_bytes():
        disk_hits.append(str(f.relative_to(site_dir)))
    if f.is_file() and (f.suffix in (".json", ".txt", ".log", ".clef") or ".db" in f.name):
        b = f.read_bytes()
        for m in EXTRA:
            if m.encode() in b:
                extra_disk[m].append(str(f.relative_to(site_dir)))

import hashlib
print(f"sentinel: length {len(sentinel)}, sha256 prefix {hashlib.sha256(sentinel.encode()).hexdigest()[:8]}")
print(f"multi-parameter routes not swept generically (runs/step-runs/trigger-data and version-history are covered above): {len(skipped)} -> {sorted(skipped)}")
print(f"API calls recorded: {calls}; statuses: {dict(sorted(statuses.items()))}")
print(f"sentinel found in API responses: {len(hits)}" + (f" -> {hits}" if hits else ""))
print(f"run details parsed: {log_entries_seen[0]}, step runs: {log_entries_seen[1]}, step-run log entries scanned (inside run detail responses): {log_entries_seen[2]}")
for m in EXTRA:
    print(f"extra marker (length {len(m)}): in API responses {len(extra_hits[m])} -> {sorted(set(extra_hits[m]))}; in files {len(extra_disk[m])} -> {extra_disk[m]}")
print(f"sentinel found in files under umbraco/: {len(disk_hits)}" + (f" -> {disk_hits}" if disk_hits else ""))
