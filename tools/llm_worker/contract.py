"""Task contract (§5): schema, semantic validation and hashing of context files."""

import hashlib
import json

from . import paths
from .config import REPO_ROOT
from .jsonschema_lite import validate

CONTRACT_VERSION = 1

# Check names each verifier produces ("schema" is implicit and always hard).
CHECKS_BY_TYPE = {
    "A1": ["types_exist", "types_complete", "methods_exist", "methods_recall", "deps_exist"],
    "A2": ["no_false_findings", "all_found", "explanations"],
    "B1": ["paths_allowed", "build", "tests_pass", "test_count", "mutation_score"],
}
CATEGORY_OF_TYPE = {"A1": "A", "A2": "A", "B1": "B"}

_GLOB_LIST = {"type": "array", "items": {"type": "string", "minLength": 1}}

CONTRACT_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "required": [
        "task_id", "version", "category", "type", "title", "instructions", "context_files",
        "allowed_paths", "forbidden_paths", "output_schema", "acceptance", "verification",
        "max_rounds", "max_candidates", "budget", "requires_review", "escalation",
    ],
    "properties": {
        # Used in file and branch names: no slashes, no spaces.
        "task_id": {"type": "string", "pattern": r"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"},
        "version": {"const": CONTRACT_VERSION},
        "category": {"enum": ["A", "B", "C"]},
        "type": {"enum": sorted(CHECKS_BY_TYPE)},
        "title": {"type": "string", "minLength": 1},
        "instructions": {"type": "string", "minLength": 1},
        "context_files": {
            "type": "array",
            "minItems": 1,
            "items": {
                "type": "object",
                "additionalProperties": False,
                "required": ["path", "sha256"],
                "properties": {
                    "path": {"type": "string", "minLength": 1},
                    "sha256": {"type": "string", "pattern": r"^[0-9a-f]{64}$"},
                },
            },
        },
        "allowed_paths": _GLOB_LIST,
        "forbidden_paths": _GLOB_LIST,
        "output_schema": {"type": "object"},
        "acceptance": {
            "type": "array",
            "minItems": 1,
            "items": {
                "type": "object",
                "additionalProperties": False,
                "required": ["check", "hard"],
                "properties": {
                    "check": {"type": "string"},
                    "hard": {"type": "boolean"},
                    "threshold": {"type": "number", "minimum": 0},
                },
            },
        },
        "verification": {"type": "object"},
        # Decision #110: attempt 0 plus at most 2 retries.
        "max_rounds": {"type": "integer", "minimum": 0, "maximum": 2},
        "max_candidates": {"type": "integer", "minimum": 1, "maximum": 2},
        "budget": {
            "type": "object",
            "additionalProperties": False,
            "required": ["max_total_tokens", "max_minutes", "max_output_tokens", "max_context_tokens"],
            "properties": {
                "max_total_tokens": {"type": "integer", "minimum": 1},
                "max_minutes": {"type": "number", "minimum": 1},
                "max_output_tokens": {"type": "integer", "minimum": 1},
                "max_context_tokens": {"type": "integer", "minimum": 1},
                # Claude tokens the same task costs when done by Claude (for the savings estimate).
                "claude_reference_tokens": {"type": "integer", "minimum": 0},
            },
        },
        "requires_review": {"type": "boolean"},
        "escalation": {"type": "string", "minLength": 1},
    },
}

_VERIFICATION_SCHEMAS = {
    "A1": {
        "type": "object",
        "additionalProperties": False,
        "required": ["source_files"],
        "properties": {"source_files": {"type": "array", "minItems": 1, "items": {"type": "string"}}},
    },
    "A2": {
        "type": "object",
        "additionalProperties": False,
        "required": ["data_file"],
        "properties": {"data_file": {"type": "string"}},
    },
    "B1": {
        "type": "object",
        "additionalProperties": False,
        "required": ["restore_cmd", "build_cmd", "test_cmd", "command_timeout_s", "mutants"],
        "properties": {
            # restore is the only networked step; build and test run with --no-restore offline.
            "restore_cmd": {"type": "array", "minItems": 1, "items": {"type": "string"}},
            "build_cmd": {"type": "array", "minItems": 1, "items": {"type": "string"}},
            "test_cmd": {"type": "array", "minItems": 1, "items": {"type": "string"}},
            "command_timeout_s": {"type": "integer", "minimum": 10},
            "mutants": {
                "type": "array",
                "minItems": 1,
                "items": {
                    "type": "object",
                    "additionalProperties": False,
                    "required": ["file", "find", "replace"],
                    "properties": {
                        "file": {"type": "string"},
                        "find": {"type": "string", "minLength": 1},
                        "replace": {"type": "string"},
                    },
                },
            },
        },
    },
}


class ContractError(ValueError):
    pass


def sha256_file(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def contract_hash(contract):
    return hashlib.sha256(json.dumps(contract, sort_keys=True, ensure_ascii=False).encode()).hexdigest()


def validate_contract(contract, repo_root=REPO_ROOT):
    """Returns a list of errors; a contract with errors must not enter the queue."""
    errors = validate(contract, CONTRACT_SCHEMA)
    if errors:
        return errors
    ctype = contract["type"]
    if contract["category"] == "C":
        errors.append("category C is Claude/owner only (§3)")
    elif CATEGORY_OF_TYPE[ctype] != contract["category"]:
        errors.append(f"type {ctype} belongs to category {CATEGORY_OF_TYPE[ctype]}")
    if contract["max_candidates"] > 1 and ctype != "B1":
        errors.append("2 candidates only for B1 (§4)")
    if contract["category"] == "A" and contract["allowed_paths"]:
        errors.append("category A writes no files: allowed_paths must be empty")
    if contract["category"] == "B" and not contract["allowed_paths"]:
        errors.append("category B needs allowed_paths")

    errors.extend(f"verification{e[1:]}" for e in validate(contract["verification"], _VERIFICATION_SCHEMAS[ctype]))

    if ctype == "B1" and isinstance(contract["verification"].get("mutants"), list):
        for i, mut in enumerate(contract["verification"]["mutants"]):
            if isinstance(mut, dict) and paths.normalize(mut.get("file")) is None:
                errors.append(f"verification.mutants[{i}].file invalid: {mut.get('file')!r}")

    names = [a["check"] for a in contract["acceptance"]]
    expected = CHECKS_BY_TYPE[ctype]
    if sorted(names) != sorted(expected):
        errors.append(f"acceptance must list each of {expected} once, got {names}")

    from .context import excluded  # local import: context imports contract helpers

    for item in contract["context_files"]:
        rel = paths.normalize(item["path"])
        if rel is None:
            errors.append(f"context file path invalid: {item['path']!r}")
            continue
        if excluded(rel):
            errors.append(f"context file excluded by config: {rel}")
            continue
        f = repo_root / rel
        if not f.is_file():
            errors.append(f"context file missing: {rel}")
        elif sha256_file(f) != item["sha256"]:
            errors.append(f"context file changed since contract was written: {rel}")
    return errors


def fill_hashes(contract, repo_root=REPO_ROOT):
    """Pilot helper: writes current sha256 of each context file into the contract (before submit)."""
    for item in contract["context_files"]:
        item["sha256"] = sha256_file(repo_root / item["path"])
    return contract
