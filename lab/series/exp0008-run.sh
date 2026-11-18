#!/usr/bin/env bash
# EXP-0008 main series (ADR-0023 п. 1, EXP-0008 П0-П11, «Runbook»). Window #355: 10 h from the driver start (#414).
# Run by the owner from a plain terminal with no Claude Code session open for the whole run (#355); IDE and browser may stay
# open, nothing needs closing (#408). Keep the laptop on mains power and do not let it sleep.
#   lab/series/exp0008-run.sh            the real series (frozen manifest; ~4.5-5 h, worst ~9-10 h)
#   lab/series/exp0008-run.sh --smoke    functional check on the smoke manifest (~3-5 min; not a measurement)
# Results: lab/results/EXP-0008/series-<UTC stamp>/ (never committed). Check progress: lab/series/exp0008-status.sh
set -euo pipefail
SMOKE=0
for a in "$@"; do
    case "$a" in
        --smoke) SMOKE=1;;
        -h|--help) sed -n '2,8p' "$0"; exit 0;;
        *) echo "unknown argument $a" >&2; exit 2;;
    esac
done
source "$(dirname "$0")/exp0008-common.sh"
EXP_MD="$REPO/development/experiments/EXP-0008-tick-benchmark-m0-rereg.md"
if [ "$SMOKE" = 1 ]; then
    MANIFEST="$REPO/lab/manifests/EXP-0008-bench-smoke.json"
    RUN_DIR="$REPO/lab/results/EXP-0008/series-smoke-$(date -u +%Y%m%dT%H%M%SZ)"
else
    MANIFEST="$REPO/lab/manifests/EXP-0008-bench.json"
    RUN_DIR="$REPO/lab/results/EXP-0008/series-$(date -u +%Y%m%dT%H%M%SZ)"
fi
mkdir -p "$RUN_DIR"
ln -sfn "$RUN_DIR" "$REPO/lab/results/EXP-0008/latest"
stage "EXP-0008 series, smoke=$SMOKE, manifest $(basename "$MANIFEST"), results $RUN_DIR"
preflight_355 "$SMOKE"
check_tree "$SMOKE"
record_env
build_lab
H=$(manifest_hash "$MANIFEST") || die "manifest hash failed for $MANIFEST" 2
echo "manifest $(basename "$MANIFEST") $H" >> "$RUN_DIR/env.txt"
if [ "$SMOKE" = 0 ]; then
    # Freeze guard (П0, ADR-0023 п. 1.10): the series runs only on the manifest whose hash the frozen pre-registration records.
    # The value may be bare, "quoted" or 'quoted', with an optional trailing # comment.
    RAW=$(sed -n 's/^manifest_hash:[[:space:]]*//p' "$EXP_MD" | head -1 | sed 's/[[:space:]]#.*$//; s/^#.*$//; s/[[:space:]]*$//; s/^"\(.*\)"$/\1/; s/^'"'"'\(.*\)'"'"'$/\1/')
    [ -n "$RAW" ] || die "EXP-0008 has no manifest_hash yet: calibrate and freeze first (Runbook step 2)" 2
    FROZEN=$(printf '%s' "$RAW" | grep -E '^[0-9a-f]{64}$' || true)
    [ -n "$FROZEN" ] || die "EXP-0008 manifest_hash is not 64 lowercase hex: '$RAW'" 2
    [ "$FROZEN" = "$H" ] || die "manifest hash $H differs from the frozen manifest_hash $FROZEN in EXP-0008" 2
    say "manifest_hash $H = frozen EXP-0008 manifest_hash"
fi
say "expected: quiet wait >= 60 s, f_ref ~26 min, validity ~23 min, 114 processes ~2 h 20 min, repeats and reference ~1.5 h"

stage "driver"
run_driver --manifest "$MANIFEST" --out "$RUN_DIR/series"
CODE=$(cat "$RUN_DIR/driver_exit_code")

stage "after the series: src/ and mods/ unchanged since core_commit (П0)"
if git -C "$REPO" diff --quiet "$CORE_COMMIT" HEAD -- src mods && [ -z "$(git -C "$REPO" status --porcelain -- src mods)" ]; then
    say "git diff $CORE_COMMIT..HEAD -- src mods: empty, working tree of src/ mods/ clean"; echo "src_mods_unchanged true" >> "$RUN_DIR/env.txt"
else
    say "WARNING: src/ or mods/ changed since $CORE_COMMIT (commits or uncommitted edits): the series is INVALID (b) «different builds»"; echo "src_mods_unchanged false" >> "$RUN_DIR/env.txt"
fi
if [ -f "$RUN_DIR/series/series.json" ] && [ "$CODE" != 2 ] && [ "$CODE" != 3 ]; then
    stage "analysis: exp0008.py"
    set +e
    python3 "$REPO/lab/analysis/exp0008.py" "$RUN_DIR/series" --out "$RUN_DIR/summary.json" 2>&1 | tee -a "$RUN_DIR/console.log"
    set -e
fi
stage "done (driver exit $CODE)"
cat <<MSG | tee -a "$RUN_DIR/console.log"

Series finished in $(( ($(date +%s) - T0) / 60 )) min, driver exit $CODE. Send back:
  $RUN_DIR/summary.json          analysis (outcome row, Р1/Р2, noise, GC, R2(г))
  $RUN_DIR/env.txt               core_commit, manifest hash, machine, src/mods check
  $RUN_DIR/console.log           everything printed
  $RUN_DIR/series/series.json    the full record
Do not delete $RUN_DIR: the EXP record cites it (lab/results/ is not committed).
MSG
