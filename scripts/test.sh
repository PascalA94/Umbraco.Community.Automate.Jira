#!/usr/bin/env bash
# Runs the test suite once per Automate line in eng/lines.txt and exits non-zero if any line fails.
# Extra arguments go to every `dotnet test` run, for example: ./scripts/test.sh --filter "FullyQualifiedName~Foo".
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

log="$(mktemp)"
trap 'rm -f "$log"' EXIT

failed=()
while IFS= read -r line || [[ -n "$line" ]]; do
  line="${line//[[:space:]]/}"
  [[ -z "$line" || "$line" == \#* ]] && continue

  echo "=== Automate line ${line} ==="
  # Every line restores for itself: the lines share obj/ folders, so a stale restore would test the wrong dependencies.
  dotnet test Umbraco.Community.Automate.Jira.slnx -p:AutomateMajor="$line" "$@" 2>&1 | tee "$log" | grep -E "^(Passed|Failed)!| error "
  status="${PIPESTATUS[0]}"

  # A zero exit with no "Passed!" line means no tests ran, which must not count as a pass.
  if [[ "$status" -ne 0 ]] || ! grep -qE "^Passed!" "$log"; then
    failed+=("$line")
  fi
done < eng/lines.txt

if ((${#failed[@]} > 0)); then
  echo "FAILED lines: ${failed[*]}" >&2
  exit 1
fi
echo "All lines passed."
