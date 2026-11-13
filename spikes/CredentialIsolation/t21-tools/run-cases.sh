#!/usr/bin/env bash
# usage: run-cases.sh <17|18> <tag> <case...>
S=$(cd "$(dirname "$0")" && pwd); L=$1; TAG=$2; shift 2
cd $S/work/CredentialIsolation; : > echo.log
for c in "$@"; do ./run-case.sh $L $c $TAG | python3 -I $S/redact.py $S/sentinel.txt | cut -c1-300; done
python3 $S/cnt.py echo.log $S/sentinel.txt
python3 - "$TAG" "$L" <<'PY'
import sys,glob
S="$S"
PY
n=0; for f in evidence/$TAG-site$L-*.json; do grep -q -F -f $S/sentinel.txt "$f" && n=$((n+1)); done; echo "evidence files containing sentinel: $n"
cp echo.log evidence/$TAG-echo-site$L.log 2>/dev/null
