#!/usr/bin/env bash
# Progress of the running EXP-0008 series or calibration (read-only; safe to run from a second terminal at any time).
#   lab/series/exp0008-status.sh [RESULTS_DIR]   default: the latest series, else the latest calibration
set -uo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
D="${1:-}"
if [ -z "$D" ]; then
    D=$(ls -dt "$REPO"/lab/results/EXP-0008/series-* "$REPO"/lab/results/EXP-0008/calibration/*/ 2>/dev/null | head -1)
fi
[ -n "$D" ] && [ -d "$D" ] || { echo "no EXP-0008 results directory found"; exit 1; }
echo "results: $D"
echo "stages:"; sed 's/^/  /' "$D/STATE" 2>/dev/null
if pgrep -f "Singularity.Lab.dll bench --mode series" > /dev/null; then
    echo "driver: RUNNING (pid $(pgrep -f 'Singularity.Lab.dll bench --mode series' | head -1))"
else
    echo "driver: not running; exit code $(cat "$D/driver_exit_code" 2>/dev/null || echo '?')"
fi
echo "attempts finished: $(ls "$D/series/attempts" 2>/dev/null | wc -l)"
echo "last log lines:"; tail -n 5 "$D/series/series.log" 2>/dev/null | sed 's/^/  /'
