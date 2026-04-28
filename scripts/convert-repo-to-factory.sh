#!/bin/bash
# Mechanical converter from `AssetHubDbContext xxx` constructor injection
# to `DbContextProvider provider` + per-method lease acquisition.
#
# Usage: convert-repo-to-factory.sh <file.cs> <originalVarName>
#   originalVarName is the parameter name used in the original constructor
#   (typically "db", "dbContext", or "context"). The body keeps that name —
#   we just inject `var <name> = lease.Db;` at the top of every method that
#   takes a CancellationToken.
#
# Idempotent enough: running twice on the same file inserts duplicate leases,
# so don't.

set -euo pipefail
file="$1"
varname="$2"

# Step 1: constructor parameter and any plain-typed positional uses.
sed -i \
    -e "s|(AssetHubDbContext $varname)|(DbContextProvider provider)|g" \
    -e "s|, AssetHubDbContext $varname,|, DbContextProvider provider,|g" \
    -e "s|, AssetHubDbContext $varname)|, DbContextProvider provider)|g" \
    -e "s|AssetHubDbContext $varname,|DbContextProvider provider,|g" \
    "$file"

# Step 2: insert lease acquisition at the top of every method body whose
# signature ends with `CancellationToken ct ...)`.
awk -v varname="$varname" '
{ lines[NR] = $0 }
END {
    for (i = 1; i <= NR; i++) {
        line = lines[i]
        print line
        # Look for a method signature line ending in "CancellationToken ct...)" or
        # "CancellationToken cancellationToken...)".
        if (match(line, /CancellationToken[ \t]+(ct|cancellationToken)([ \t]*=[ \t]*default)?\)[ \t]*$/) ) {
            # The next non-blank line should be the opening brace.
            j = i + 1
            while (j <= NR && lines[j] ~ /^[ \t]*$/) {
                print lines[j]
                j++
            }
            if (j <= NR && lines[j] ~ /^[ \t]*\{[ \t]*$/) {
                print lines[j]
                # Match indent of the brace + 4 spaces for body.
                indent = lines[j]
                sub(/[^ \t].*$/, "", indent)
                print indent "    await using var lease = await provider.AcquireAsync(" \
                      ((line ~ /cancellationToken/) ? "cancellationToken" : "ct") ");"
                print indent "    var " varname " = lease.Db;"
                i = j  # skip the printed brace
            }
        }
    }
}
' "$file" > "$file.tmp" && mv "$file.tmp" "$file"
