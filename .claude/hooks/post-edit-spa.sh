#!/usr/bin/env bash
#
# PostToolUse hook for Write|Edit: the per-edit quality layer for the Angular workspace.
#
# WHY THIS LAYER EXISTS
# ---------------------
# test-plan.md risk #7: an SPA regression reaches production because no gate runs the frontend specs
# or lint. CI is now that gate, but CI answers minutes later. This hook is the only layer that can
# hand the failure back to the agent IN-SESSION, so a formatting slip or a broken colocated spec gets
# fixed in the next turn instead of at commit time.
#
# WHAT IT DELIBERATELY DOES NOT DO
# --------------------------------
# - It never runs the whole suite. `npm test` is ~60s of tests on top of ~20s of bundling; paying that
#   per edit would make the agent loop unusable. Only the edited file's own colocated spec runs.
# - It never runs the backend tests. `dotnet test` starts a SQL Server container (30-60s) - that is a
#   commit/CI-layer check, not a per-edit one.
# - It never rewrites the edited file. Prettier runs in --check mode: a hook that edits files behind
#   the agent's back makes the next diff a mystery.
#
# SCOPE OF THE SPEC RUN
# ---------------------
# Lint and format run on every touched SPA file, because they cost milliseconds. The spec run is
# limited to core/ and shared/ - the two hot-spot directories risk #7 names (112 and 77 file-changes
# in 30 days). Elsewhere the bundling cost buys too little.
#
# EXIT CODES (see CLAUDE.md)
#   0 - nothing to do, or everything passed.
#   2 - blocking: stdout reaches the agent as feedback, which is the whole point of the hook.
set -uo pipefail

PAYLOAD=$(cat)

REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null)}"
if [ -z "$REPO_ROOT" ]; then
  exit 0
fi
APP_DIR="$REPO_ROOT/src/app"

# node, NOT jq: jq is not guaranteed on a Windows dev machine, whereas node is a hard requirement of
# this repo (the Angular CLI refuses to start without it). It also does the path arithmetic properly -
# the payload carries an absolute Windows path with backslashes, and what the tools want is a path
# relative to src/app. Prints nothing when the edit landed outside src/app, which is the common case.
# Exit code 3 means "I could not find a file path in the payload" - kept DISTINCT from "the path was
# not interesting" (empty stdout, exit 0). An earlier version collapsed the two, and a hook that
# silently succeeds on input it could not read is indistinguishable from one that checked a clean
# file. That was found during this phase's verification, with a malformed payload that parsed as
# nothing: every check was skipped and the hook still reported success.
REL=$(printf '%s' "$PAYLOAD" | node -e '
  let raw = "";
  process.stdin.on("data", (d) => (raw += d));
  process.stdin.on("end", () => {
    let p = "";
    try {
      p = JSON.parse(raw)?.tool_input?.file_path ?? "";
    } catch {
      p = "";
    }
    if (!p) {
      process.exitCode = 3;
      return;
    }
    const path = require("node:path");
    const app = path.resolve(process.argv[1]);
    const abs = path.resolve(p.replace(/\\/g, "/"));
    const rel = path.relative(app, abs).split(path.sep).join("/");
    // ".." means the file is outside the workspace; "" means it IS the workspace directory.
    if (rel && !rel.startsWith("..")) process.stdout.write(rel);
  });
' "$APP_DIR")
NODE_STATUS=$?

# Non-blocking (exit 0) but never silent: the edit itself was fine, this hook was not. Blocking here
# would stall the agent over a hook bug; saying nothing would hide it for the rest of the session.
if [ "$NODE_STATUS" -ne 0 ]; then
  printf '%s\n' "post-edit-spa.sh: no tool_input.file_path in the hook payload (node exit ${NODE_STATUS}); NO check ran for this edit." >&2
  exit 0
fi

if [ -z "$REL" ]; then
  exit 0
fi

case "$REL" in
  *.ts | *.html | *.scss) ;;
  *) exit 0 ;;
esac

cd "$APP_DIR" || exit 0

REPORT=""
FAILED=0

# Collects output rather than streaming it, so one run reports every failing check instead of stopping
# at the first - the agent can then fix the formatting AND the lint error in a single turn.
check() {
  local label="$1"
  shift

  local output
  if ! output=$("$@" 2>&1); then
    FAILED=1
    REPORT="${REPORT}${label} failed for ${REL}:"$'\n'"${output}"$'\n\n'
  fi
}

check "Prettier" npx prettier --check "$REL"

# eslint's lintFilePatterns cover ts and html only (see src/app/eslint.config.js); handing it a .scss
# file is an error about the file type, not about the file.
case "$REL" in
  *.ts | *.html) check "ESLint" npx eslint "$REL" ;;
esac

# The colocated spec, when this file is in a hot-spot directory and has one. `ng test --include`
# accepts a file path directly, and --watch defaults to false outside a TTY, so the run terminates.
SPEC=""
case "$REL" in
  src/app/core/* | src/app/shared/*)
    case "$REL" in
      *.spec.ts) SPEC="$REL" ;;
      *.ts)
        candidate="${REL%.ts}.spec.ts"
        if [ -f "$candidate" ]; then
          SPEC="$candidate"
        fi
        ;;
    esac
    ;;
esac

if [ -n "$SPEC" ]; then
  check "Spec run ($SPEC)" npx ng test --include "$SPEC"
fi

if [ "$FAILED" -ne 0 ]; then
  printf '%s' "$REPORT"
  exit 2
fi

exit 0
