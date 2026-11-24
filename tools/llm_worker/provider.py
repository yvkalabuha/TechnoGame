"""OpenAI-compatible chat provider (llama.cpp server). The API key is never logged or stored."""

import ipaddress
import json
import os
import socket
import stat
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field

from . import config, gitleaks


class ProviderUnavailable(RuntimeError):
    """Server unreachable, timed out or returned 5xx after all retries. The task must be requeued."""


class ProviderError(RuntimeError):
    """Non-retryable error (4xx, malformed response)."""


class AuthError(RuntimeError):
    """HTTP 401/403: the key is wrong for every task, so the whole run stops and the task is requeued."""


class InsufficientFunds(RuntimeError):
    """HTTP 402: no balance. The whole run stops and the task is requeued; nothing is retried."""


class KeyFileError(RuntimeError):
    """key_error: the key file is missing, empty, not 0600, not ours or a symlink. The message has no key."""


class NotJson(ProviderError):
    """HTTP 200 whose body is not JSON (retried: a billed response is already on the ledger)."""


@dataclass
class Completion:
    content: str
    usage: dict = field(default_factory=dict)
    timings: dict = field(default_factory=dict)
    elapsed_s: float = 0.0
    finish_reason: str = ""
    reasoning_chars: int = 0  # length of `reasoning_content`; the text itself is dropped, never parsed
    reasoning_tokens: int | None = None


def parse_response(raw, elapsed_s=0.0):
    """Raw HTTP body bytes -> Completion. Also used to resume from a saved raw body without a new request."""
    try:
        payload = json.loads(raw.decode("utf-8"))
    except (ValueError, UnicodeDecodeError):
        raise NotJson("response is not JSON") from None
    try:
        content = payload["choices"][0]["message"]["content"] or ""
    except (KeyError, IndexError, TypeError):
        raise ProviderError("malformed response: no choices[0].message.content") from None
    usage = payload.get("usage") or {}
    msg = payload["choices"][0]["message"]
    reasoning = msg.get("reasoning_content") if isinstance(msg, dict) else None
    details = usage.get("completion_tokens_details") if isinstance(usage, dict) else None
    rt = details.get("reasoning_tokens") if isinstance(details, dict) else None
    finish = payload["choices"][0].get("finish_reason")
    return Completion(content=content, usage=usage, timings=payload.get("timings") or {},
                      elapsed_s=elapsed_s, finish_reason=finish if isinstance(finish, str) else "",
                      reasoning_chars=len(reasoning) if isinstance(reasoning, str) else 0,
                      reasoning_tokens=rt if type(rt) is int else None)


def _usage_of(raw):
    """usage from a raw body that is JSON but has no usable answer; {} when there is none."""
    try:
        usage = json.loads(raw.decode("utf-8")).get("usage")
    except (ValueError, UnicodeDecodeError, AttributeError):
        return {}
    return usage if isinstance(usage, dict) else {}


def _local_host(url):
    """Loopback or private address: where a free (unmetered) provider may live."""
    host = urllib.parse.urlparse(url).hostname or ""
    if host == "localhost":
        return True
    try:
        ip = ipaddress.ip_address(host)
    except ValueError:
        return False
    return ip.is_loopback or ip.is_private


_EXAMPLE = {"string": "text", "integer": 0, "number": 0, "boolean": False, "null": None}


def _example(schema, depth=0):
    """A short example object for the prompt: the JSON mode wants one."""
    t = schema.get("type") if isinstance(schema, dict) else None
    if t == "object" and depth < 3:
        return {k: _example(v, depth + 1) for k, v in schema.get("properties", {}).items()}
    if t == "array" and depth < 3:
        return [_example(schema.get("items", {}), depth + 1)]
    return _EXAMPLE.get(t)


def _text_of(content):
    """Text of a message; for a multi-part message (text + image_url) only the text parts."""
    if isinstance(content, list):
        return "\n".join(p.get("text", "") for p in content if isinstance(p, dict) and p.get("type") == "text")
    return str(content)


def load_strict_key(path):
    """Key of a paid provider: only from `path`, mode exactly 0600, owned by this user, not a symlink."""
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    except OSError as e:
        raise KeyFileError(f"key_error: cannot open key file {path} ({e.strerror})") from None
    with os.fdopen(fd, encoding="utf-8", errors="replace") as f:
        st = os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode) or stat.S_IMODE(st.st_mode) != 0o600 or st.st_uid != os.getuid():
            raise KeyFileError(f"key_error: {path} must be a regular file, mode 0600, owned by this user")
        key = f.read().strip()
    if not key:
        raise KeyFileError(f"key_error: key file {path} is empty")
    return key


def load_api_key(environ=os.environ, env_name=config.API_KEY_ENV, file_env=config.API_KEY_FILE_ENV,
                 default_file=config.DEFAULT_API_KEY_FILE):
    """The key from `env_name`, else from the file named by `file_env` (default `default_file`).
    Returns '' when neither is set. The key is never printed, logged or stored in run records."""
    key = environ.get(env_name, "").strip()
    if key:
        return key
    path = environ.get(file_env, default_file)
    try:
        with open(path, encoding="utf-8") as f:
            return f.read().strip()
    except OSError:
        return ""


class OpenAICompatProvider:
    def __init__(self, url=None, model=None, api_key=None, profile=None, guard=None,
                 timeout_s=config.REQUEST_TIMEOUT_S, retries=config.PROVIDER_RETRIES,
                 retry_pause_s=config.PROVIDER_RETRY_PAUSE_S, enable_thinking=False,
                 thinking=False, effort=config.DEFAULT_REASONING_EFFORT,
                 opener=urllib.request.urlopen, sleep=time.sleep):
        self.profile = profile or config.PROFILES["llamacpp"]
        self.name = self.profile.name
        self.paid = self.profile.paid
        self.url = url or self.profile.url
        if not self.paid and not self.profile.free_public and not _local_host(self.url):
            raise ValueError("a free profile may only talk to a loopback or private address "
                             "(a public host is a paid service: use a paid profile with the ledger)")
        self.model = model or self.profile.model
        p = self.profile
        if api_key is not None:
            self._api_key = api_key
        elif p.strict_key:
            self._api_key = load_strict_key(p.key_file)
        else:
            self._api_key = load_api_key(os.environ, p.key_env, p.key_file_env, p.key_file)
        self.guard = guard  # ledger guard: required for a paid profile
        self.timeout_s = timeout_s
        self.retries = retries
        self.retry_pause_s = retry_pause_s
        self.enable_thinking = enable_thinking  # llama.cpp template switch
        if effort not in config.REASONING_EFFORTS:
            raise ValueError(f"bad reasoning effort {effort!r}")
        self.thinking = thinking  # DeepSeek thinking mode
        self.effort = effort
        self._opener = opener
        self._sleep = sleep

    def __repr__(self):
        return f"OpenAICompatProvider(profile={self.name!r}, url={self.url!r}, model={self.model!r})"

    def body(self, messages, schema, schema_name, max_tokens, seed=None):
        p = self.profile
        extra = json.loads(json.dumps(p.extra_body))
        if "chat_template_kwargs" in extra:
            extra["chat_template_kwargs"]["enable_thinking"] = self.enable_thinking
        body = {"model": self.model, "messages": messages, "max_tokens": max_tokens, "temperature": 0.2, **extra}
        if "thinking" in extra and self.thinking:
            body["thinking"] = {"type": "enabled"}
            body["reasoning_effort"] = self.effort
            del body["temperature"]
        if p.send_seed:
            body["seed"] = 0 if seed is None else seed
        if p.json_mode == "json_schema":
            body["response_format"] = {"type": "json_schema", "json_schema": {"name": schema_name, "schema": schema}}
        else:
            # JSON mode needs the word "json" and an example in the prompt; the schema goes there. The answer
            # is validated locally by the loop.
            note = ("Reply only with one json object that matches this JSON schema "
                    f"('{schema_name}'):\n{json.dumps(schema, ensure_ascii=False)}\n"
                    f"Example of the shape (json): {json.dumps(_example(schema), ensure_ascii=False)}")
            msgs = [dict(m) for m in messages]
            if msgs and msgs[0].get("role") == "system" and isinstance(msgs[0]["content"], str):
                msgs[0]["content"] = f"{msgs[0]['content']}\n\n{note}"
            else:
                msgs.insert(0, {"role": "system", "content": note})
            body["messages"] = msgs
            body["response_format"] = {"type": "json_object"}
        return body

    def complete(self, messages, schema, schema_name, max_tokens, seed=None, on_raw=None):
        """`on_raw(bytes)` gets the raw response body before it is parsed, so a paid answer is never lost."""
        if self.paid and self.guard is None:
            raise RuntimeError("paid provider without a spend guard")
        if self.paid or self.profile.free_public:
            # Everything that leaves the machine, retry prompts with check output included (ADR-0015 §8).
            # Raw contents, one line per newline: JSON escaping would blur the rules, and one long line would let
            # a single `gitleaks:allow` hide everything.
            gitleaks.scan_text("\n".join(_text_of(m.get("content", "")) for m in messages))
        data = json.dumps(self.body(messages, schema, schema_name, max_tokens, seed)).encode()
        headers = {"Content-Type": "application/json"}
        if self._api_key:
            headers["Authorization"] = f"Bearer {self._api_key}"
        last = None
        for attempt in range(self.retries + 1):
            if attempt:
                self._sleep(self.retry_pause_s * attempt)
            # Every attempt is reserved before the request, retries included. A failed attempt that may have
            # been billed (timeout, 5xx) keeps its worst-case reservation.
            rid = self.guard.before_attempt(data, max_tokens) if self.guard else None
            started = time.monotonic()
            try:
                req = urllib.request.Request(self.url, data=data, headers=headers, method="POST")
                with self._opener(req, timeout=self.timeout_s) as resp:
                    raw = resp.read()
            except urllib.error.HTTPError as e:
                if rid and e.code < 500:
                    self.guard.release(rid)  # 4xx (402, 429 included) is not billed; 5xx may be
                if e.code == 402:
                    raise InsufficientFunds("HTTP 402: insufficient balance") from None
                if e.code in (401, 403):
                    raise AuthError(f"HTTP {e.code}: check the API key") from None
                if e.code < 500 and e.code != 429:
                    raise ProviderError(f"HTTP {e.code}") from None
                last = f"HTTP {e.code}"
                continue
            except (urllib.error.URLError, socket.timeout, TimeoutError, ConnectionError, OSError) as e:
                reason = getattr(e, "reason", e)
                if rid and isinstance(reason, (socket.gaierror, ConnectionRefusedError)):
                    self.guard.release(rid)  # provably never sent; timeouts and resets may have been billed
                last = f"{type(e).__name__}: {getattr(e, 'reason', e)}"
                continue
            if on_raw:
                on_raw(raw)
            try:
                comp = parse_response(raw, round(time.monotonic() - started, 1))
            except NotJson as e:
                last = str(e)
                continue
            except ProviderError:
                if rid:
                    self.guard.settle(rid, _usage_of(raw))  # billed answer without choices: book its usage
                raise
            if rid:
                self.guard.settle(rid, comp.usage)
            return comp
        raise ProviderUnavailable(f"after {self.retries + 1} attempts: {last}")
