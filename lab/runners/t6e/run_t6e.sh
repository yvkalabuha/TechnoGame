#!/usr/bin/env bash
# EXP-0003 (T6e) runner: archive copy of the commit, harness dropped into the Session test assembly, Release build,
# golden checks, then the 350 runs in N parallel shards. Output: lab/results/t6e/ (never committed).
# Usage: lab/runners/t6e/run_t6e.sh [commit] [shards]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
COMMIT="${1:-HEAD}"
SHARDS="${2:-4}"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
RES="$ROOT/lab/results/t6e"
REPO="$RES/repo"
MANIFEST="$ROOT/lab/manifests/EXP-0003-t6e.json"
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
  TEST --filter "FullyQualifiedName~LinkedRunTests|FullyQualifiedName~ProductAiSessionTests" > "$RES/log/golden.log" 2>&1 \
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
