# Shared helpers of the EXP-0008 owner scripts (sourced, not run). ADR-0023 п. 1.1 / 1.10, EXP-0008 «Runbook».
# shellcheck shell=bash

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
LAB_DLL="$REPO/src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab.dll"
T0=$(date +%s)

say() { printf '%s [+%4ds] %s\n' "$(date -u +%H:%M:%S)" "$(( $(date +%s) - T0 ))" "$*" | tee -a "$RUN_DIR/console.log"; }
die() { say "STOP: $*"; echo "stopped: $*" >> "$RUN_DIR/STATE"; exit "${2:-2}"; }
stage() { echo "$(date -u +%FT%TZ) $*" >> "$RUN_DIR/STATE"; say "== $*"; }

# #355: no Claude sessions; other forbidden names are only listed (the driver decides by CPU, ADR-0023 п. 1.1).
preflight_355() {
    local smoke="$1" claude others
    claude=$(ps -eo pid=,comm= | awk 'tolower($2) ~ /^claude/ {print $1"("$2")"}' | tr '\n' ' ')
    others=$(ps -eo pid=,comm= | awk 'tolower($2) ~ /^(node|msbuild|vbcscompiler)/ {print $1"("$2")"}' | tr '\n' ' ')
    [ -n "$others" ] && say "note: idle processes with forbidden names (fatal only if they use >= 0.05 CPU during the run): $others"
    if [ -n "$claude" ]; then
        if [ "$smoke" = 1 ]; then say "smoke: Claude processes present, ignored: $claude"
        else die "Claude processes are running ($claude). Close every Claude Code session first (#355)." 2; fi
    fi
}

# П0: code under measurement = HEAD main, clean src/ and mods/.
check_tree() {
    local smoke="$1"
    CORE_COMMIT=$(git -C "$REPO" rev-parse HEAD)
    local dirty; dirty=$(git -C "$REPO" status --porcelain -- src mods)
    if [ -n "$dirty" ]; then
        if [ "$smoke" = 1 ]; then say "smoke: src/ or mods/ has uncommitted changes, ignored"
        else die "src/ or mods/ has uncommitted changes (EXP-0008 П0 needs dirty = false): $(echo "$dirty" | head -3 | tr '\n' ' ')" 2; fi
    fi
    say "core_commit $CORE_COMMIT (branch $(git -C "$REPO" rev-parse --abbrev-ref HEAD))"
    echo "core_commit $CORE_COMMIT" >> "$RUN_DIR/env.txt"
}

build_lab() {
    stage "build (Release, no build servers left behind)"
    "$DOTNET" build "$REPO/src/Singularity.Lab" -c Release -nodeReuse:false -p:UseSharedCompilation=false -v quiet -nologo \
        > "$RUN_DIR/build.log" 2>&1 || die "build failed, see $RUN_DIR/build.log" 1
    "$DOTNET" build-server shutdown > /dev/null 2>&1 || true
    [ -f "$LAB_DLL" ] || die "no $LAB_DLL after build" 1
}

# Prints the 64-hex manifest_hash of a bench manifest; fails (status 1) if the plan fails or prints no hash.
manifest_hash() {
    local h
    h=$("$DOTNET" "$LAB_DLL" bench --mode plan --manifest "$1" | sed -n 's/^manifest_hash=\([0-9a-f]\{64\}\).*/\1/p') || return 1
    [ -n "$h" ] || return 1
    echo "$h"
}

record_env() {
    {
        echo "started_utc $(date -u +%FT%TZ)"
        echo "host $(hostname)"
        echo "cpus $(nproc)"
        echo "governor $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_governor 2>/dev/null || echo n/a)"
        echo "power_supply_online $(cat /sys/class/power_supply/A*/online 2>/dev/null | head -1 || echo n/a)"
        echo "dotnet $("$DOTNET" --version)"
    } >> "$RUN_DIR/env.txt"
}

# The driver: one child process at a time; output into console.log and series/series.log.
run_driver() {
    local code
    set +e
    "$DOTNET" "$LAB_DLL" bench --mode series "$@" 2>&1 | tee -a "$RUN_DIR/console.log"
    code=${PIPESTATUS[0]}
    set -e
    echo "$code"  > "$RUN_DIR/driver_exit_code"
    case "$code" in
        0) say "driver exit 0: done";;
        1) say "driver exit 1: INVALID (a) - a child process failed or crashed";;
        2) say "driver exit 2: usage or manifest error";;
        3) say "driver exit 3: NOT STARTED - no quiet machine before the deadline (top foreign processes in series.log)";;
        4) say "driver exit 4: INCONCLUSIVE by exclusions (reference part still ran)";;
        5) say "driver exit 5: INVALID (b) - a forbidden process (#355) used CPU";;
        *) say "driver exit $code";;
    esac
    return 0
}
