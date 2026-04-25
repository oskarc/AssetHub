#!/usr/bin/env bash
# Dump all open SonarCloud issues for a project to a flat list.
#
# Usage:
#   SONAR_TOKEN=<your-user-token> SONAR_KEY=<project-key> ./scripts/sonar-issues.sh
#
# Output: scripts/sonar-issues.txt — one line per issue:
#   <severity>  <rule>  <file>:<line>  <message>
#
# Token: SonarCloud → My Account → Security → Generate Token (type: User).
# Project key: visible in the URL of your project's SonarCloud page.

set -euo pipefail

: "${SONAR_TOKEN:?Set SONAR_TOKEN to a SonarCloud user token (My Account → Security)}"
: "${SONAR_KEY:?Set SONAR_KEY to your SonarCloud project key}"

OUT="scripts/sonar-issues.txt"
TMP="$(mktemp)"
PAGE=1
TOTAL=0

mkdir -p scripts
: > "$OUT"

while :; do
    curl -sS -u "${SONAR_TOKEN}:" \
        "https://sonarcloud.io/api/issues/search?componentKeys=${SONAR_KEY}&resolved=false&ps=500&p=${PAGE}" \
        > "$TMP"

    COUNT=$(jq '.issues | length' "$TMP")
    if [[ "$COUNT" -eq 0 ]]; then break; fi

    jq -r '.issues[] | "\(.severity)\t\(.rule)\t\(.component | sub("^[^:]+:";""))\t\(.line // 0)\t\(.message)"' "$TMP" \
        >> "$OUT"

    TOTAL=$((TOTAL + COUNT))
    PAGE=$((PAGE + 1))

    # SonarCloud caps at 10000 results per query; one page = 500.
    if [[ "$COUNT" -lt 500 ]]; then break; fi
done

rm -f "$TMP"
echo "Wrote ${TOTAL} issues to ${OUT}"
echo ""
echo "Top rules:"
cut -f2 "$OUT" | sort | uniq -c | sort -rn | head -20
