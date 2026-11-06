#!/usr/bin/env bash
# Fails when docs/ holds anything but the four files that may be public, or when any tracked file is named
# like a private review artefact. The repo is public, so a stray review, council transcript or evidence file
# is a leak, not a tidy-up.
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

allowed=$'docs/design.md\ndocs/plan.md\ndocs/spikes.md\ndocs/runbook.md'
failed=0

while IFS= read -r file; do
  [[ -z "$file" ]] && continue
  if ! grep -qxF -- "$file" <<<"$allowed"; then
    echo "docs-guard: not allowed in docs/: $file" >&2
    failed=1
  fi
done < <(git ls-files docs)

# Matches on the file name only. Content is scanned by gitleaks. Separators vary, so allow - _ or space.
private_names='security[-_ ]?review|plan[-_ ]?review|council|(^|/)scope\.md$|s7[-_ ]?core[-_ ]?gap'
while IFS= read -r file; do
  [[ -z "$file" ]] && continue
  echo "docs-guard: private artefact name tracked: $file" >&2
  failed=1
done < <(git ls-files | grep -iE "$private_names")

# History too: a private file committed and later deleted stays readable in a public repo.
# Needs a full clone (fetch-depth: 0 in CI); a repo with no commits has no history to check.
if git rev-parse -q --verify HEAD >/dev/null; then
  while IFS= read -r file; do
    [[ -z "$file" ]] && continue
    echo "docs-guard: private artefact name in history: $file" >&2
    failed=1
  done < <(git log --all --name-only --format= | sort -u | grep -iE "$private_names")
fi

if [[ $failed -ne 0 ]]; then
  exit 1
fi
echo "docs-guard: ok"
