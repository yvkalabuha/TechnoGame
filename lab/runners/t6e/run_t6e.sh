#!/usr/bin/env bash
# EXP-0003 (T6e) / EXP-0004 (T6e2) / EXP-0005 (T6e3) runner: archive copy of the commit, harness dropped into the Session test assembly, Release build,
# golden checks, then the runs of the manifest in N parallel shards. Output: lab/results/<name>/ (never committed).
# Usage: lab/runners/t6e/run_t6e.sh [commit] [shards]
#   env T6E_NAME (default t6e) — results dir; T6E_MANIFEST_FILE (default lab/manifests/EXP-0003-t6e.json);
#   T6E_GOLDEN_FILTER — extra dotnet test filter terms for the golden step (EXP-0004: F17bTests).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
COMMIT="${1:-HEAD}"
SHARDS="${2:-4}"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
RES="$ROOT/lab/results/${T6E_NAME:-t6e}"
REPO="$RES/repo"
MANIFEST="$ROOT/${T6E_MANIFEST_FILE:-lab/manifests/EXP-0003-t6e.json}"
mkdir -p "$RES/out" "$RES/log"
if [ ! -d "$REPO/src" ]; then
  mkdir -p "$REPO"
  git -C "$ROOT" archive "$COMMIT" | tar -x -C "$REPO"
  git -C "$ROOT" rev-parse "$COMMIT" > "$RES/core_commit.txt"
fi
cp "$ROOT/lab/runners/t6e/T6eHarness.cs" "$REPO/tests/Singularity.Session.Tests/T6eHarness.cs"
"$DOTNET" build "$REPO/tests/Singularity.Session.Tests" -c Release -v q -nologo > "$RES/log/build.log"
TEST() { "$DOTNET" test "$REPO/tests/Singularity.Session.Tests" -c Release --no-build -nologo "$@"; }
if [ "${SKIP_GOLDEN:-0}" != 1 ]; then
  TEST --filter "FullyQualifiedName~LinkedRunTests|FullyQualifiedName~ProductAiSessionTests${T6E_GOLDEN_FILTER:-}" > "$RES/log/golden.log" 2>&1 \
    && echo "golden: pass" || { echo "golden: FAIL (see $RES/log/golden.log)"; exit 1; }
fi
pids=()
for ((i = 0; i < SHARDS; i++)); do
  T6E_MANIFEST="$MANIFEST" T6E_OUT="$RES/out" T6E_SHARD="$i/$SHARDS" \
    TEST --filter "FullyQualifiedName~T6eHarness" --logger "console;verbosity=normal" > "$RES/log/shard$i.log" 2>&1 &
  pids+=($!)
done
for p in "${pids[@]}"; do wait "$p"; done
echo "runs: $(find "$RES/out" -name run.json | wc -l)"
