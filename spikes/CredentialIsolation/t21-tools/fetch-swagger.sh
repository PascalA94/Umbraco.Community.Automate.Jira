#!/usr/bin/env bash
# usage: fetch-swagger.sh <httpsPort> <label>
S=$(dirname "$0"); P=$1; L=$2
T=$(curl -sk -X POST https://localhost:$P/umbraco/management/api/v1/security/back-office/token -d grant_type=client_credentials -d client_id=umbraco-back-office-t21 --data-urlencode "client_secret@$S/client-secret.txt" | python3 -c "import sys,json;print(json.load(sys.stdin).get('access_token',''))")
echo "token length ${#T}"
for d in management automate-management; do
  for u in "openapi/$d.json" "swagger/$d/swagger.json"; do
    c=$(curl -sk -H "Authorization: Bearer $T" -o $S/swagger/$L-$d.json -w "%{http_code}" https://localhost:$P/umbraco/$u); echo "$d $u $c"; [ "$c" = 200 ] && break
  done
done
