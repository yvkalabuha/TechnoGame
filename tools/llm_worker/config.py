"""Static configuration. Runtime overrides come from CLI flags or environment variables."""

import json
import os
from dataclasses import dataclass, field
from pathlib import Path

PACKAGE_DIR = Path(__file__).resolve().parent
REPO_ROOT = PACKAGE_DIR.parent.parent

DEFAULT_URL = os.environ.get("LLM_WORKER_URL", "http://10.8.0.1:8090/v1/chat/completions")
DEFAULT_MODEL = os.environ.get("LLM_WORKER_MODEL", "qwen3.6-35b-a3b")
API_KEY_ENV = "LLM_WORKER_API_KEY"
API_KEY_FILE_ENV = "LLM_WORKER_API_KEY_FILE"
DEFAULT_API_KEY_FILE = "/home/user/.config/local-llm/key"
# DeepSeek key (ADR-0015 §8): a 0600 file outside the repository, read from nowhere else.
DEEPSEEK_KEY_FILE = str(Path.home() / ".config" / "singularity" / "deepseek-api-key")



@dataclass(frozen=True)
class Profile:
    """Request shape of one provider. json_mode: 'json_schema' (llama.cpp grammar, schema in the request)
    or 'json_object' (schema only in the prompt; the answer is validated locally)."""
    name: str
    url: str
    model: str
    json_mode: str
    extra_body: dict = field(default_factory=dict)
    send_seed: bool = True
    paid: bool = False
    key_env: str = API_KEY_ENV
    key_file_env: str = API_KEY_FILE_ENV
    key_file: str = DEFAULT_API_KEY_FILE
    strict_key: bool = False  # key only from key_file, which must be 0600 and ours; no environment variable


# DeepSeek names, URL, JSON mode and thinking switch checked on api-docs.deepseek.com
# (quick_start/pricing, guides/json_mode, guides/thinking_mode) on 2026-11-13.
# Thinking is on by default there, so it is disabled explicitly. `seed` is not documented: not sent.
PROFILES = {
    "llamacpp": Profile("llamacpp", DEFAULT_URL, DEFAULT_MODEL, "json_schema",
                        {"chat_template_kwargs": {"enable_thinking": False}}),
    "deepseek-flash": Profile("deepseek-flash", "https://api.deepseek.com/chat/completions", "deepseek-flash",
                              "json_object", {"thinking": {"type": "disabled"}}, send_seed=False, paid=True,
                              key_file=DEEPSEEK_KEY_FILE, strict_key=True),
    "deepseek-v4-pro": Profile("deepseek-v4-pro", "https://api.deepseek.com/chat/completions", "deepseek-v4-pro",
                            "json_object", {"thinking": {"type": "disabled"}}, send_seed=False, paid=True,
                            key_file=DEEPSEEK_KEY_FILE, strict_key=True),
}

# Spend accounting (ADR-0015 §6). The ledger lives outside the repository.
LEDGER_DIR = Path(os.environ.get("XDG_DATA_HOME") or Path.home() / ".local" / "share") / "singularity-llm"
PRICES_FILE = PACKAGE_DIR / "prices.json"
# Paid provider limits (ADR-0015 §6, decision #175 d).
MODEL_FAILURE_LIMIT = 2   # model failures per (step, model) pair
MAX_INFRA_FAILURES = 3    # outages per task before it is escalated

# Output is 7-9 tok/s: a long answer takes many minutes. The server itself cuts at 7200 s.
REQUEST_TIMEOUT_S = 3600
PROVIDER_RETRIES = 3
PROVIDER_RETRY_PAUSE_S = 30

# Runtime directories (git-ignored).
QUEUE_DIR = PACKAGE_DIR / "queue"
RUNS_DIR = PACKAGE_DIR / "runs"
TASKS_DIR = REPO_ROOT / "development" / "tasks"
REPORTS_DIR = PACKAGE_DIR / "reports"
# Outside the repository: a broken worktree must never resolve to the main tree by walking up.
WORKTREES_DIR = Path(os.environ.get("XDG_CACHE_HOME") or Path.home() / ".cache") / "llm_worker" / "worktrees"
LESSONS_DIR = PACKAGE_DIR / "lessons"

# Never sent to the model, whatever the contract says (§7).
CONTEXT_EXCLUDE = [
    ".env",
    ".env.*",
    "**/.env",
    "**/.env.*",
    "**/*.pem",
    "**/*.key",
    "*.pem",
    "*.key",
    "**/.ssh/**",
    ".ssh/**",
    "lab/results/**",
    ".git/**",
    "**/.git/**",
    ".mcp.json*",
    "**/.mcp.json*",
    ".claude/settings.local.json",
    "tools/llm_worker/queue/**",
    "tools/llm_worker/runs/**",
    "tools/llm_worker/reports/**",
    "**/worktrees/**",
    "**/id_rsa*",
    "**/id_ed25519*",
    "**/id_ecdsa*",
    "**/*.p12",
    "**/*.pfx",
    "**/*secret*",
    "**/credentials*",
]

# Never written by the tool, whatever the contract says (decision #110): R2 + R3 of risk_paths.json.
RISK_PATHS_FILE = PACKAGE_DIR / "risk_paths.json"
_RISK = json.loads(RISK_PATHS_FILE.read_text(encoding="utf-8"))
WRITE_FORBIDDEN = _RISK["R2"] + _RISK["R3"]
# Decision #179: creating a new file here is allowed even under an R2 glob; overwriting an existing one is not.
WRITE_NEW_ALLOWED = _RISK["R1_if_added"]
WRITE_NEW_EXCEPT = _RISK["R1_if_added_except"]
WRITE_NEVER = _RISK["R3"]

# Rough token estimate for budget checks; Qwen tokenizer is not available offline.
CHARS_PER_TOKEN = 3.5
