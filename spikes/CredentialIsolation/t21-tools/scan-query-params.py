#!/usr/bin/env python3
"""t21 supplement: GET routes that have REQUIRED query parameters (the main scanner calls plain GETs bare, so these answered 400).
Fills uuid params with GUIDs seen, ints with 0, bools false, strings 'a'. Also covers path-param GETs whose params are not GUIDs
(string/int) with a few probe values. Counts only. usage: scan-query-params.py <https-port> <client-id> <secret-file> <sentinel> <doc-style 17|18> <label>"""
import json, re, ssl, sys, urllib.request, urllib.error, urllib.parse, collections, itertools
port, cid, secf, sen, line, label = sys.argv[1:7]
base = f"https://localhost:{port}"; secret = open(secf).read().strip()
ctx = ssl.create_default_context(); ctx.check_hostname = False; ctx.verify_mode = ssl.CERT_NONE
GUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
def req(method, path, tok=None, data=None):
    r = urllib.request.Request(base + path, method=method, data=data)
    if tok: r.add_header("Authorization", "Bearer " + tok)
    try:
        with urllib.request.urlopen(r, context=ctx, timeout=30) as x: return x.status, x.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e: return e.code, e.read().decode("utf-8", "replace")
    except Exception as e: return 0, type(e).__name__
def token():
    return json.loads(req("POST", "/umbraco/management/api/v1/security/back-office/token", data=urllib.parse.urlencode({"grant_type":"client_credentials","client_id":cid,"client_secret":secret}).encode())[1])["access_token"]
tok = token(); ids = set(); calls = 0; hits = []; st = collections.Counter(); covered = collections.Counter(); skipped = []
def rec(label_, s, t):
    global calls
    calls += 1; st[s] += 1; ids.update(GUID.findall(t.lower()))
    if sen in t: hits.append(label_)
docs = {}
for doc in ("management", "automate-management"):
    s, t = req("GET", f"/umbraco/openapi/{doc}.json") if line == "18" else (404, "")
    if s != 200: s, t = req("GET", f"/umbraco/swagger/{doc}/swagger.json")
    docs[doc] = json.loads(t)
# seed GUIDs from every plain GET
for doc, spec in docs.items():
    for p, ops in spec["paths"].items():
        if "get" in ops and "{" not in p and not any(x.get("required") and x.get("in") == "query" for x in ops["get"].get("parameters", [])):
            rec(p, *req("GET", p, tok))
def resolve(spec, prm):
    if "$ref" in prm: return spec["components"]["parameters"][prm["$ref"].split("/")[-1]]
    return prm
def vals(prm):
    sch = prm.get("schema", {}); t = sch.get("type"); f = sch.get("format")
    if f == "uuid": return sorted(ids)[:25]
    if t == "integer": return ["0", "1"]
    if t == "boolean": return ["false"]
    if t == "array": return vals({"schema": sch.get("items", {})})[:5]
    if sch.get("enum"): return [str(e) for e in sch["enum"]][:5]
    return ["a", "test"]
for rnd in range(2):
    tok = token()
    for doc, spec in docs.items():
        for p, ops in spec["paths"].items():
            op = ops.get("get")
            if not op: continue
            prms = [resolve(spec, x) for x in op.get("parameters", [])] + [resolve(spec, x) for x in ops.get("parameters", [])]
            need = [x for x in prms if x.get("required") and x.get("in") in ("query", "path")]
            if not need: continue
            if rnd == 0 and any(x["in"] == "path" and x.get("schema", {}).get("format") == "uuid" and len([y for y in need if y["in"] == "path"]) == 1 and not any(y["in"] == "query" for y in need) for x in need):
                continue  # pure single-GUID-path routes: already covered by the main scanner
            combos = [vals(x) for x in need]
            n = 0
            for combo in itertools.islice(itertools.product(*combos), 60):
                path = p; q = []
                for x, v in zip(need, combo):
                    if x["in"] == "path": path = path.replace("{" + x["name"] + "}", urllib.parse.quote(v))
                    else: q.append(f"{x['name']}={urllib.parse.quote(v)}")
                if "{" in path: skipped.append(p); break
                rec(f"GET {p}", *req("GET", path + ("?" + "&".join(q) if q else ""), tok)); n += 1
            if n: covered[p] += n
elem = sorted(p for p in covered if "element" in p or "library" in p.lower())
print(f"[{label}] supplementary calls: {calls}; statuses {dict(sorted(st.items()))}; routes with required params exercised: {len(covered)}; skipped (unfilled path params): {len(set(skipped))}")
print(f"  element/Library/recycle-bin routes exercised here: {len(elem)}")
print(f"  sentinel found in responses: {len(hits)}" + (f" -> {sorted(set(hits))}" if hits else ""))
