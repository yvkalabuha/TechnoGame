#!/usr/bin/env bash
# EXP-0008 calibration of the (н1)/(н2) thresholds (ADR-0023 п. 1.10, EXP-0008 П0, П5.6, «Runbook»).
# Run by the owner from a plain terminal with no Claude Code session open (#355); IDE and browser may stay open (#408).
#   lab/series/exp0008-calibrate.sh            real calibration (block A, 15 processes, ~40-60 min)
#   lab/series/exp0008-calibrate.sh --smoke    functional check on the smoke manifest (~1-2 min; not a calibration)
# Results: lab/results/EXP-0008/calibration/<UTC stamp>/ (never committed). Send back the files printed at the end.
set -euo pipefail
SMOKE=0
for a in "$@"; do
    case "$a" in
        --smoke) SMOKE=1;;
        -h|--help) sed -n '2,7p' "$0"; exit 0;;
        *) echo "unknown argument $a" >&2; exit 2;;
    esac
done
source "$(dirname "$0")/exp0008-common.sh"
if [ "$SMOKE" = 1 ]; then
    MANIFEST="$REPO/lab/manifests/EXP-0008-bench-smoke.json"
    RUN_DIR="$REPO/lab/results/EXP-0008/calibration/smoke-$(date -u +%Y%m%dT%H%M%SZ)"
else
    MANIFEST="$REPO/lab/manifests/EXP-0008-calibration.json"
    RUN_DIR="$REPO/lab/results/EXP-0008/calibration/$(date -u +%Y%m%dT%H%M%SZ)"
fi
mkdir -p "$RUN_DIR"
stage "EXP-0008 calibration (ADR-0023 п. 1.10), smoke=$SMOKE, manifest $(basename "$MANIFEST"), results $RUN_DIR"
preflight_355 "$SMOKE"
check_tree "$SMOKE"
record_env
build_lab
H=$(manifest_hash "$MANIFEST"); [ -n "$H" ] || die "manifest hash failed for $MANIFEST" 2
echo "calibration_manifest $(basename "$MANIFEST") $H" >> "$RUN_DIR/env.txt"
say "calibration manifest_hash $H"
say "expected: wait for quiet >= 60 s, f_ref 5 min, then 15 processes of point A (t8), ~2-3 min each; progress: tail -f $RUN_DIR/series/series.log"

stage "driver --calibration true"
run_driver --manifest "$MANIFEST" --out "$RUN_DIR/series" --calibration true
CODE=$(cat "$RUN_DIR/driver_exit_code")
[ "$CODE" = 0 ] || die "the driver did not finish the calibration (exit $CODE); send $RUN_DIR/console.log and series/series.log" "$CODE"

stage "thresholds: exp0008.py --calibrate"
python3 "$REPO/lab/analysis/exp0008.py" "$RUN_DIR/series" --calibrate --out "$RUN_DIR/calibrate.json" | tee -a "$RUN_DIR/console.log"
stage "done"
cat <<MSG | tee -a "$RUN_DIR/console.log"

Calibration finished in $(( ($(date +%s) - T0) / 60 )) min. Send back (paste or attach):
  $RUN_DIR/calibrate.json        proposed thresholds (p90 rule of П5.6) and the raw windows
  $RUN_DIR/env.txt               core_commit, manifest hash, machine
  $RUN_DIR/series/series.log     the run log (foreign CPU, run delay, T per process)
  $RUN_DIR/series/series.json    full record (optional, ~100 KB)
Nothing is changed in the repository: the Orchestrator writes the numbers into EXP-0008 П5.6 and the manifest.
MSG
