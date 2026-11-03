"""Static configuration. Runtime overrides come from CLI flags or environment variables."""

import os
from pathlib import Path

PACKAGE_DIR = Path(__file__).resolve().parent
REPO_ROOT = PACKAGE_DIR.parent.parent

DEFAULT_URL = os.environ.get("LLM_WORKER_URL", "http://10.8.0.1:8090/v1/chat/completions")
DEFAULT_MODEL = os.environ.get("LLM_WORKER_MODEL", "qwen3.6-35b-a3b")
API_KEY_ENV = "LLM_WORKER_API_KEY"
API_KEY_FILE_ENV = "LLM_WORKER_API_KEY_FILE"
DEFAULT_API_KEY_FILE = "/home/user/.config/local-llm/key"

# Output is 7-9 tok/s: a long answer takes many minutes. The server itself cuts at 7200 s.
REQUEST_TIMEOUT_S = 3600
PROVIDER_RETRIES = 3
PROVIDER_RETRY_PAUSE_S = 30

# Runtime directories (git-ignored).
QUEUE_DIR = PACKAGE_DIR / "queue"
RUNS_DIR = PACKAGE_DIR / "runs"
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

# Never written by the tool, whatever the contract says (decision #110: no edits to the game core).
WRITE_FORBIDDEN = [
    "src/Singularity.Core/**",
    ".git/**",
    ".github/**",
    ".githooks/**",
    ".claude/**",
    "CLAUDE.md",
    "**/*.csproj",
    "**/Directory.Build.props",
]

# Rough token estimate for budget checks; Qwen tokenizer is not available offline.
CHARS_PER_TOKEN = 3.5
