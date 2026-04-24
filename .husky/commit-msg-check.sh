#!/bin/sh
# Conventional Commits validator
# https://www.conventionalcommits.org/en/v1.0.0/

msg_file="$1"
first_line=$(head -n 1 "$msg_file" | sed -e 's/\r$//')

# Allow merge, revert, and fixup commits to pass through
case "$first_line" in
  Merge*|Revert*|fixup!*|squash!*)
    exit 0
    ;;
esac

pattern='^(feat|fix|chore|docs|test|refactor|style|perf|ci|build|revert)(\([a-z0-9 ._-]+\))?!?: .{1,}'

if ! echo "$first_line" | grep -qE "$pattern"; then
  echo ""
  echo "  ✖ Commit message nesplňuje Conventional Commits."
  echo ""
  echo "  Formát:  <type>(scope)?: <description>"
  echo "  Types:   feat, fix, chore, docs, test, refactor, style, perf, ci, build, revert"
  echo ""
  echo "  Příklad: feat(auth): add JWT refresh rotation"
  echo "           fix: correct RLS policy on boilers table"
  echo "           chore(ci): cache NuGet packages"
  echo ""
  echo "  Tvá zpráva: $first_line"
  echo ""
  exit 1
fi
