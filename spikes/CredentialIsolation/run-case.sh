#!/usr/bin/env bash
# Spike-only: run one isolation case on a site and print a one-line summary.
# Usage: run-case.sh <17|18> <case> <evidence-prefix>
set -u
site=$1; case=$2; tag=$3
out="evidence/$tag-site$site-$case.json"
code=$(curl -s -o "$out" -w "%{http_code}" -X POST "http://localhost:51$site/spike/run/$case")
python3 - "$out" "$code" "$site" "$case" <<'PY'
import json,sys
p,code,site,case=sys.argv[1:]
try:
    d=json.load(open(p))
except Exception:
    print(f"site{site} {case}: HTTP {code}, not JSON"); sys.exit()
s=(d.get('steps') or [{}])[0]
print(f"site{site} {case}: run={d.get('runStatus')} step={s.get('status')} cat={s.get('category')} err={(s.get('error') or '')[:160]!r} out={(s.get('outputData') or '')[:140]!r}")
PY
