#!/usr/bin/env bash
# Privacy scan (context.spec C6), shared by /save-context and the pre-commit hook.
# Usage: privacy-scan.sh <file>...   Prints each hit as file:line:text.
# Exit 0 = clean, 1 = hits found, 2 = usage or setup error.
set -u

root="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "privacy-scan: not in a git repo" >&2; exit 2; }
committed="$root/.claude/hooks/privacy-denylist.txt"
local_terms="$root/.githooks/privacy-denylist.local.txt"
[ "$#" -gt 0 ] || { echo "usage: privacy-scan.sh <file>..." >&2; exit 2; }
[ -f "$committed" ] || { echo "privacy-scan: missing $committed" >&2; exit 2; }

patterns="$(mktemp)"
trap 'rm -f "$patterns" "$patterns.deny" "$patterns.allow"' EXIT
cat "$committed" > "$patterns"
[ -f "$local_terms" ] && cat "$local_terms" >> "$patterns"
grep -vE '^(#|!|$)' "$patterns" | tr -d '\r' > "$patterns.deny"
sed -n 's/^!//p' "$patterns" | tr -d '\r' > "$patterns.allow"

# The denylist matches itself, so it is never scanned. Raster images are skipped too (guardrails A5):
# their compressed bytes match the text patterns by chance (seven random bytes in a PNG read as an email), and
# the ones this repo commits are the design screenshots, rendered pixels with no text metadata. Every
# other file, binary or not, is scanned.
files=()
for f in "$@"; do
  [ "$f" -ef "$committed" ] && continue
  case "$(printf '%s' "$f" | tr '[:upper:]' '[:lower:]')" in
    *.png|*.jpg|*.jpeg|*.gif|*.webp) continue ;;
  esac
  files+=("$f")
done
[ "${#files[@]}" -gt 0 ] || exit 0

hits="$(grep -nHiE -f "$patterns.deny" -- "${files[@]}" 2>/dev/null)"
if [ -s "$patterns.allow" ] && [ -n "$hits" ]; then
  hits="$(printf '%s\n' "$hits" | grep -viE -f "$patterns.allow")"
fi

if [ -n "$hits" ]; then
  printf '%s\n' "$hits"
  exit 1
fi
exit 0
