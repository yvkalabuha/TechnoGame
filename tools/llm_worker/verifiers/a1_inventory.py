"""A1: code inventory. Every reported name is searched in the source; recall is measured
against a regex scan of type and method declarations (not Roslyn: good enough for the pilot)."""

import re

from .common import make_report, ratio, short_list

_TYPE_RE = re.compile(r"\b(class|struct|interface|enum|record(?:\s+struct|\s+class)?)\s+([A-Za-z_]\w*)")
_MODIFIERS = r"(?:(?:public|private|protected|internal|static|override|virtual|abstract|sealed|async|partial|new|extern|unsafe|readonly)\s+)+"
# Return type is required, so constructors, operators and properties do not match.
_RETURN_TYPE = r"(?:\([^()]*\)|[\w.]+(?:<[^()]*?>)?)(?:\[\])*\??"
_METHOD_RE = re.compile(
    r"^\s*" + _MODIFIERS + _RETURN_TYPE + r"\s+([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(",
    re.MULTILINE,
)


def strip_comments(text):
    """Removes comments and string/char literals (approximate: no verbatim or raw strings)."""
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.DOTALL)
    text = re.sub(r"//[^\n]*", "", text)
    text = re.sub(r'"(?:\\.|[^"\\\n])*"', '""', text)
    return re.sub(r"'(?:\\.|[^'\\\n])'", "''", text)


def scan(text):
    code = strip_comments(text)
    types = {m.group(2) for m in _TYPE_RE.finditer(code)}
    methods = {m.group(1) for m in _METHOD_RE.finditer(code)} - types
    return types, methods


def measure(artifact, source_texts):
    """artifact: worker output; source_texts: {path: text}. Returns {check: (value, detail)}."""
    ref_types, ref_methods, code = set(), set(), {}
    for path, text in sorted(source_texts.items()):
        t, m = scan(text)
        ref_types |= {(path, x) for x in t}
        ref_methods |= {(path, x) for x in m}
        code[path] = strip_comments(text)

    rep_types, rep_methods, rep_deps = set(), set(), []
    for t in artifact["types"]:
        rep_types.add((t["file"], t["name"]))
        rep_methods |= {(t["file"], m) for m in t["methods"]}
        rep_deps += [(t["file"], d) for d in t["depends_on"]]

    bad_types = rep_types - ref_types
    missed_types = ref_types - rep_types

    def method_found(path, name):
        return path in code and re.search(rf"\b{re.escape(name)}\s*(?:<[^>()]*>)?\s*\(", code[path]) is not None

    bad_methods = {pm for pm in rep_methods if not method_found(*pm)}
    missed_methods = ref_methods - rep_methods
    bad_deps = {pd for pd in rep_deps if pd[0] not in code or not re.search(rf"\b{re.escape(pd[1])}\b", code[pd[0]])}

    fmt = lambda pairs: short_list(f"{p}:{n}" for p, n in pairs)
    return {
        "types_exist": (ratio(len(rep_types) - len(bad_types), len(rep_types)),
                        f"not declared in that file: {fmt(bad_types)}" if bad_types else "ok"),
        "types_complete": (ratio(len(ref_types) - len(missed_types), len(ref_types)),
                           f"missing types: {fmt(missed_types)}" if missed_types else "ok"),
        "methods_exist": (ratio(len(rep_methods) - len(bad_methods), len(rep_methods)),
                          f"no such method in file: {fmt(bad_methods)}" if bad_methods else "ok"),
        "methods_recall": (ratio(len(ref_methods) - len(missed_methods), len(ref_methods)),
                           f"missing methods: {fmt(missed_methods)}" if missed_methods else "ok"),
        "deps_exist": (ratio(len(rep_deps) - len(bad_deps), len(rep_deps)),
                       f"dependency names not in file: {fmt(bad_deps)}" if bad_deps else "ok"),
    }


class A1Verifier:
    def __init__(self, contract, repo_root):
        self.contract = contract
        self.sources = {p: (repo_root / p).read_text(encoding="utf-8")
                        for p in contract["verification"]["source_files"]}

    def verify(self, artifact):
        return make_report(measure(artifact, self.sources), self.contract["acceptance"])

    def finalize(self, artifact):
        return []
