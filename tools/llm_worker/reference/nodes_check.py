"""Deterministic reference check of a node table (jet-slice nodes.csv format).

Findings are (check, node_id, ref) tuples, sorted:
  duplicate_id      node_id = repeated id
  duplicate_idx     node_id = repeated idx
  dangling_requires node_id requires ref, which does not exist
  cycle             node_id lies on a requires cycle
  generation_order  node_id (gen Gk) requires ref with a later generation
  start_closure     an actor knows node_id at start but none of the alternatives in ref
                    (ref = '|'-joined group) for some actor; node_id is suffixed '@ACTOR'

Usage: python3 -m tools.llm_worker.reference.nodes_check docs/research/data/jet-slice/nodes.csv
"""

import csv
import sys


def parse_requires(text):
    """'a;b|c' -> [['a'], ['b', 'c']]: ';' is AND, '|' is an alternative group."""
    groups = []
    for part in (text or "").split(";"):
        alts = [x.strip() for x in part.split("|") if x.strip()]
        if alts:
            groups.append(alts)
    return groups


def _gen(value):
    v = (value or "").strip()
    return int(v[1:]) if len(v) > 1 and v[0] == "G" and v[1:].isdigit() else None


def load(path):
    with open(path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def check_rows(rows):
    findings = set()
    seen_id, seen_idx = set(), set()
    for r in rows:
        if r["id"] in seen_id:
            findings.add(("duplicate_id", r["id"], ""))
        seen_id.add(r["id"])
        if r["idx"] in seen_idx:
            findings.add(("duplicate_idx", r["idx"], ""))
        seen_idx.add(r["idx"])

    by_id = {}
    for r in rows:
        by_id.setdefault(r["id"], r)
    edges = {}
    for r in rows:
        deps = sorted({x for g in parse_requires(r["requires"]) for x in g})
        edges.setdefault(r["id"], set()).update(deps)
        for d in deps:
            if d not in by_id:
                findings.add(("dangling_requires", r["id"], d))
            else:
                g_node, g_dep = _gen(r["gen"]), _gen(by_id[d]["gen"])
                if g_node is not None and g_dep is not None and g_dep > g_node:
                    findings.add(("generation_order", r["id"], d))

    for node in _cycle_nodes({k: sorted(v & by_id.keys()) for k, v in edges.items()}):
        findings.add(("cycle", node, ""))

    actors = sorted({a for r in rows for a in (r.get("known_at_start") or "").split(";")
                     if a.strip() and a.strip() != "ALL"})

    def knows(row, actor):
        k = [a.strip() for a in (row.get("known_at_start") or "").split(";") if a.strip()]
        return "ALL" in k or actor in k

    for r in rows:
        for actor in actors:
            if not knows(r, actor):
                continue
            for group in parse_requires(r["requires"]):
                if not any(g in by_id and knows(by_id[g], actor) for g in group):
                    findings.add(("start_closure", f"{r['id']}@{actor}", "|".join(group)))
    return sorted(findings)


def _cycle_nodes(graph):
    """Nodes in a strongly connected component of size > 1 or with a self-loop (iterative Tarjan)."""
    index, low, on_stack, stack, out = {}, {}, set(), [], set()
    counter = 0
    for root in sorted(graph):
        if root in index:
            continue
        work = [(root, 0)]
        while work:
            v, i = work.pop()
            if i == 0:
                index[v] = low[v] = counter
                counter += 1
                stack.append(v)
                on_stack.add(v)
            succ = graph.get(v, [])
            if i < len(succ):
                work.append((v, i + 1))
                w = succ[i]
                if w not in index:
                    work.append((w, 0))
                elif w in on_stack:
                    low[v] = min(low[v], index[w])
                continue
            if low[v] == index[v]:
                comp = []
                while True:
                    w = stack.pop()
                    on_stack.discard(w)
                    comp.append(w)
                    if w == v:
                        break
                if len(comp) > 1 or v in graph.get(v, []):
                    out.update(comp)
            if work:
                parent = work[-1][0]
                low[parent] = min(low[parent], low[v])
    return sorted(out)


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    findings = check_rows(load(argv[1]))
    for f in findings:
        print("\t".join(f))
    print(f"# {len(findings)} findings", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
