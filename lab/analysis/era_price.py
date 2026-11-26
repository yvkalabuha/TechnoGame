#!/usr/bin/env python3
"""TASK-0195: per-actor table of the era node price pilot (docs/simulation/era-node-price.md).

For each matrix directory (one curve each; slot series, counters SlotProbe slot_*) prints per actor:
  RET2  money of science returned to the budget after 1990, share of the science money (median, p10..p90 over seeds; goal < 20 %);
  GAP   largest interval between discoveries, 1947-1990 (gate GAP36, max over seeds) and after 1990 (max over seeds);
  G2/G3/G4  first unit of the actor (median year, +inf = never);
  IDLE  idle slot-months 1945-1990 and 1991-2030 (median; max over seeds);
  SHORT months with money shortage (u < 1) after 1990, share (median);
  KN    knowledge nodes known at the horizon (median) and of them nodes with anchor year >= 1990 (median);
  --years: per actor and every 5th year from 1990, the money returned that year (median %) / share of slot-months on nodes anchored >= 1990
        (SlotProbe slot_years_{A}, TASK-0195).
Usage: python3 lab/analysis/era_price.py NODES_YAML DIR [DIR ...] [--years]. Exit 0, 2 = usage. Test: lab/analysis/test_era_price.py.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import slot_core as sc  # noqa: E402

ACTORS = ("USA", "SUN", "GBR", "FRA", "SWE")


def anchors(nodes_yaml):
    """node number -> anchor year (0 = none), regex parse as in pace_check."""
    with open(nodes_yaml, encoding="utf-8") as f:
        text = f.read()
    out = {}
    for block in re.split(r"\n  - id:", text)[1:]:
        num = re.search(r"\n    number: (\d+)", block)
        yr = re.search(r"\n    anchor:\n      year: (\d+)", block)
        if num:
            out[int(num.group(1))] = int(yr.group(1)) if yr else 0
    return out


def share(num, den):
    return int(num) / max(1, int(den))


def actor_rows(d, anchor):
    runs, first, known, summary, info, elapsed, actors = sc.load(d)
    rs = sorted(r for r, v in runs.items() if v["status"] == "ok" and v["threads"] == "1")
    late = {}
    for row in sc.read_csv(d, "node_times.csv"):
        t = row["t_known"]
        if t not in ("", None) and int(t) >= -1:
            k = (row["run"], row["actor"])
            n = int(row["node"])
            late.setdefault(k, [0, 0])
            late[k][0] += 1
            late[k][1] += 1 if anchor.get(n, 0) >= 1990 else 0
    out = {}
    for a in [x for x in ACTORS if x in actors]:
        i = {r: info[r] for r in rs}
        ret2 = [share(i[r][f"slot_p2_{a}_returned_raw"], i[r][f"slot_p2_{a}_science_raw"]) for r in rs]
        wl = {r: int(i[r][f"slot_{a}_workable_last"]) for r in rs}
        gap1 = [sc.max_gap(known[r][a], wl[r], sc.GATE_FIRST, sc.PERIOD1_LAST) for r in rs]
        gap2 = [sc.max_gap(known[r][a], wl[r], sc.PERIOD1_LAST + 1) for r in rs]
        idle1 = [share(i[r][f"slot_p1_{a}_idle"], i[r][f"slot_p1_{a}_slot_months"]) for r in rs]
        idle2 = [share(i[r][f"slot_p2_{a}_idle"], i[r][f"slot_p2_{a}_slot_months"]) for r in rs]
        short2 = [share(i[r][f"slot_p2_{a}_short_months"], i[r][f"slot_p2_{a}_months"]) for r in rs]
        kn = [late.get((r, a), [0, 0]) for r in rs]
        out[a] = {
            "ret2": (sc.med(ret2), sc.pct(ret2, 0.1), sc.pct(ret2, 0.9)),
            "gap1": max(gap1), "gap2": max(gap2),
            "g": tuple(sc.med([first[r].get((a, g), sc.INF) for r in rs]) for g in sc.GENS),
            "idle1": (sc.med(idle1), max(idle1)), "idle2": (sc.med(idle2), max(idle2)),
            "short2": sc.med(short2),
            "kn": (sc.med([k[0] for k in kn]), sc.med([k[1] for k in kn])),
            "n": len(rs),
        }
    return out


def year_returns(d, actor):
    """year -> (median returned share, median share of slot-months on nodes anchored >= 1990) over the ok threads-1 runs."""
    runs, _, _, _, info, _, _ = sc.load(d)
    acc = {}
    for r, v in runs.items():
        if v["status"] != "ok" or v["threads"] != "1":
            continue
        for e in filter(None, info[r].get(f"slot_years_{actor}", "").split(";")):
            y, ppm, sm, new = (int(x) for x in e.split(":"))
            acc.setdefault(y, []).append((ppm / 1e6, new / max(1, sm)))
    return {y: (sc.med([x[0] for x in xs]), sc.med([x[1] for x in xs])) for y, xs in sorted(acc.items())}


def render_years(d, actors=ACTORS, step=5):
    lines = []
    for a in actors:
        yr = year_returns(d, a)
        if yr:
            lines.append(f"  {a}: " + " ".join(f"{y}:{v[0]:.0%}/{v[1]:.0%}" for y, v in yr.items() if y >= 1990 and (y - 1990) % step == 0))
    return "\n".join(lines)


def render(name, rows):
    lines = [f"== {name}",
             "| actor | RET2 med (p10..p90) | GAP 47-90 / 91-30 | G2 / G3 / G4 | idle 45-90 / 91-30 (max) | short 91-30 | nodes known / anchor>=1990 |",
             "|---|---|---|---|---|---|---|"]
    for a, v in rows.items():
        r = v["ret2"]
        lines.append(f"| {a} | {r[0]:.1%} ({r[1]:.1%}..{r[2]:.1%}) | {v['gap1']} / {v['gap2']} | "
                     + " / ".join(sc.fmt(y) for y in v["g"])
                     + f" | {v['idle1'][0]:.1%} / {v['idle2'][0]:.1%} ({max(v['idle1'][1], v['idle2'][1]):.1%}) | {v['short2']:.0%} | {v['kn'][0]} / {v['kn'][1]} |")
    return "\n".join(lines)


def main(argv, out=print):
    years = "--years" in argv
    argv = [x for x in argv if x != "--years"]
    if len(argv) < 2 or any(x.startswith("--") for x in argv):
        out(__doc__)
        return 2
    anchor = anchors(argv[0])
    for d in argv[1:]:
        out(render(d, actor_rows(d, anchor)))
        if years:
            out(render_years(d))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
