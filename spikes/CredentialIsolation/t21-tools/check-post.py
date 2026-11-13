#!/usr/bin/env python3
"""t21 extra check 1/3: POST catalogue/step-types/{alias}/outcomes and /output-schema for every step type
with settings holding whole-value and embedded references to the package credential key and a Secrets control.
Counts only; never prints the sentinel. usage: check-post.py <https-port> <client-id> <secret-file> <sentinel-file> <outdir> <label>"""
import json, ssl, sys, urllib.request, urllib.error, urllib.parse, pathlib, collections
port, cid, secf, senf, outdir, label = sys.argv[1:7]
base = f"https://localhost:{port}"; sen = open(senf).read().strip(); secret = open(secf).read().strip()
ctx = ssl.create_default_context(); ctx.check_hostname = False; ctx.verify_mode = ssl.CERT_NONE
def req(method, path, tok=None, data=None, js=None):
    r = urllib.request.Request(base + path, method=method, data=data)
    if tok: r.add_header("Authorization", "Bearer " + tok)
    if js is not None:
        r.data = json.dumps(js).encode(); r.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(r, context=ctx, timeout=30) as x: return x.status, x.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e: return e.code, e.read().decode("utf-8", "replace")
tok = json.loads(req("POST", "/umbraco/management/api/v1/security/back-office/token", data=urllib.parse.urlencode({"grant_type":"client_credentials","client_id":cid,"client_secret":secret}).encode())[1])["access_token"]
A = "/umbraco/automate/management/api/v1"
_, st = req("GET", A + "/catalogue/step-types", tok); types = json.loads(st)
PKG = "$Umbraco:Automate:Jira:Credentials:spike:ApiToken"; CTL = "$Umbraco:Automate:Secrets:SpikeX"
variants = {"pkg-whole": lambda: PKG, "pkg-embedded": lambda: "x " + PKG + " y", "ctl-whole": lambda: CTL, "ctl-embedded": lambda: "x " + CTL + " y",
            "pkg-whole-under-key": None}
tally = collections.Counter(); rows = []
CTLVAL = "SECRET-SPIKE-X"
def payloads(f, ref):
    k = f["key"]
    if f["valueKind"] == "Collection":
        yield {k: [{"key": "X-Test", "value": ref}]}
    else:
        yield {k: ref}
for t in types:
    for f in (t.get("settingsSchema") or {}).get("fields", []):
        for ep in ("outcomes", "output-schema"):
            for vname, ref in (("pkg-whole", PKG), ("pkg-embedded", "x " + PKG + " y"), ("ctl-whole", CTL), ("ctl-embedded", "x " + CTL + " y")):
                for settings in payloads(f, ref):
                    status, body = req("POST", f"{A}/catalogue/step-types/{urllib.parse.quote(t['alias'])}/{ep}", tok, js={"settings": settings})
                    tally["calls"] += 1; tally[f"status {status}"] += 1
                    if sen in body: tally["SENTINEL in response"] += 1; rows.append((t["alias"], f["key"], ep, vname, status))
                    if CTLVAL in body: tally["control value in response"] += 1
                    if status == 400:
                        kind = "not permitted" if "not permitted" in body else "Cannot convert" if "Cannot convert" in body else "sensitive-field" if "ensitive" in body else "json-type" if "JSON value could not be converted" in body else "other"
                        tally[f"400 {kind} [{vname.split('-')[0]}-{vname.split('-')[1]}]"] += 1
                        if kind == "other": open("other-400-sample.txt","a").write(vname+" "+body.replace(sen,"<SENTINEL>")[:300]+"\n")
                    if "Cannot convert" in body: tally["Cannot-convert message seen"] += 1
                    if "Umbraco:Automate:Jira:Credentials" in body: tally["response names credentials key path (not value)"] += 1
print(f"[{label}] step types: {len(types)}; dynamic outcomes: {sum(1 for t in types if t.get('hasDynamicOutcomes'))}; dynamic output schema: {sum(1 for t in types if t.get('hasDynamicOutputSchema'))}")
for k, v in sorted(tally.items()): print(f"  {k}: {v}")
print("  sentinel hits:", rows)
