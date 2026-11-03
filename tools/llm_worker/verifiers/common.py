"""Check results and reports shared by all verifiers. The worker's self-assessment is never used."""

import hashlib


class VerifierConfigError(RuntimeError):
    """The contract's verification setup is broken (not the worker's fault): task FAILED, unverified."""


def make_report(measures, acceptance, scope_violation=False):
    """measures: {check: (value, detail)}. Thresholds default to 1.0; passed = value >= threshold."""
    checks = []
    for a in acceptance:
        value, detail = measures.get(a["check"], (0.0, "not run"))
        threshold = a.get("threshold", 1.0)
        checks.append({
            "check": a["check"], "hard": a["hard"], "value": round(float(value), 4),
            "threshold": threshold, "passed": value >= threshold, "detail": detail,
        })
    return _finish(checks, scope_violation)


def schema_failure_report(acceptance, errors):
    checks = [{"check": "schema", "hard": True, "value": 0.0, "threshold": 1.0, "passed": False,
               "detail": "; ".join(errors[:20])}]
    checks += [{"check": a["check"], "hard": a["hard"], "value": 0.0, "threshold": a.get("threshold", 1.0),
                "passed": False, "detail": "not run: output failed schema"} for a in acceptance]
    return _finish(checks, False)


def _finish(checks, scope_violation):
    if checks[0]["check"] != "schema":
        checks.insert(0, {"check": "schema", "hard": True, "value": 1.0, "threshold": 1.0,
                          "passed": True, "detail": "ok"})
    failed_hard = [c["check"] for c in checks if c["hard"] and not c["passed"]]
    # Hard checks weigh 2, soft 1; partial credit up to the threshold.
    quality = 0.0
    for c in checks:
        share = 1.0 if c["threshold"] == 0 else min(c["value"] / c["threshold"], 1.0)
        quality += (2.0 if c["hard"] else 1.0) * share
    sig_src = "|".join(f"{c['check']}:{c['detail']}" for c in checks if not c["passed"])
    return {
        "checks": checks,
        "hard_passed": not failed_hard,
        "failed_hard": failed_hard,
        "quality": round(quality, 4),
        "scope_violation": scope_violation,
        "signature": hashlib.sha256(sig_src.encode()).hexdigest()[:16],
    }


def ratio(hit, total):
    return 1.0 if total == 0 else hit / total


def short_list(items, limit=15):
    items = sorted(items)
    more = f" (+{len(items) - limit} more)" if len(items) > limit else ""
    return ", ".join(items[:limit]) + more
