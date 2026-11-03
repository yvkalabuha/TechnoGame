"""A2: data check. The worker's findings are compared with the deterministic reference script."""

from ..reference.nodes_check import check_rows, load
from .common import make_report, ratio, short_list

MIN_EXPLANATION_CHARS = 20


def measure(artifact, reference):
    ref = set(reference)
    rep = {(f["check"], f["node_id"], f.get("ref", "")) for f in artifact["findings"]}
    false = rep - ref
    missed = ref - rep
    explained = sum(1 for f in artifact["findings"] if len(f.get("explanation", "").strip()) >= MIN_EXPLANATION_CHARS)
    fmt = lambda s: short_list("/".join(x for x in f if x) for f in s)
    return {
        "no_false_findings": (ratio(len(rep) - len(false), len(rep)),
                              f"not confirmed by reference: {fmt(false)}" if false else "ok"),
        "all_found": (ratio(len(ref) - len(missed), len(ref)),
                      f"missed {len(missed)} finding(s) of types: {short_list({m[0] for m in missed})}"
                      if missed else "ok"),
        "explanations": (ratio(explained, len(artifact["findings"])),
                         "ok" if explained == len(artifact["findings"])
                         else f"findings without a {MIN_EXPLANATION_CHARS}+ char explanation"),
    }


class A2Verifier:
    def __init__(self, contract, repo_root):
        self.contract = contract
        self.reference = check_rows(load(repo_root / contract["verification"]["data_file"]))

    def verify(self, artifact):
        return make_report(measure(artifact, self.reference), self.contract["acceptance"])

    def finalize(self, artifact):
        return []
