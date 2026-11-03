"""OpenAI-compatible chat provider (llama.cpp server). The API key is never logged or stored."""

import json
import os
import socket
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field

from . import config


class ProviderUnavailable(RuntimeError):
    """Server unreachable, timed out or returned 5xx after all retries. The task must be requeued."""


class ProviderError(RuntimeError):
    """Non-retryable error (4xx, malformed response)."""


class AuthError(RuntimeError):
    """HTTP 401/403: the key is wrong for every task, so the whole run stops and the task is requeued."""


@dataclass
class Completion:
    content: str
    usage: dict = field(default_factory=dict)
    timings: dict = field(default_factory=dict)
    elapsed_s: float = 0.0


def load_api_key(environ=os.environ):
    """LLM_WORKER_API_KEY, else the file named by LLM_WORKER_API_KEY_FILE (default in config).
    Returns '' when neither is set. The key is never printed, logged or stored in run records."""
    key = environ.get(config.API_KEY_ENV, "").strip()
    if key:
        return key
    path = environ.get(config.API_KEY_FILE_ENV, config.DEFAULT_API_KEY_FILE)
    try:
        with open(path, encoding="utf-8") as f:
            return f.read().strip()
    except OSError:
        return ""


class OpenAICompatProvider:
    def __init__(self, url=config.DEFAULT_URL, model=config.DEFAULT_MODEL, api_key=None,
                 timeout_s=config.REQUEST_TIMEOUT_S, retries=config.PROVIDER_RETRIES,
                 retry_pause_s=config.PROVIDER_RETRY_PAUSE_S, enable_thinking=False,
                 opener=urllib.request.urlopen, sleep=time.sleep):
        self.url = url
        self.model = model
        self._api_key = api_key if api_key is not None else load_api_key()
        self.timeout_s = timeout_s
        self.retries = retries
        self.retry_pause_s = retry_pause_s
        self.enable_thinking = enable_thinking
        self._opener = opener
        self._sleep = sleep

    def __repr__(self):
        return f"OpenAICompatProvider(url={self.url!r}, model={self.model!r})"

    def body(self, messages, schema, schema_name, max_tokens, seed=None):
        return {
            "model": self.model,
            "messages": messages,
            "max_tokens": max_tokens,
            "temperature": 0.2,
            "seed": 0 if seed is None else seed,
            "chat_template_kwargs": {"enable_thinking": self.enable_thinking},
            "response_format": {
                "type": "json_schema",
                "json_schema": {"name": schema_name, "schema": schema},
            },
        }

    def complete(self, messages, schema, schema_name, max_tokens, seed=None):
        data = json.dumps(self.body(messages, schema, schema_name, max_tokens, seed)).encode()
        headers = {"Content-Type": "application/json"}
        if self._api_key:
            headers["Authorization"] = f"Bearer {self._api_key}"
        last = None
        for attempt in range(self.retries + 1):
            if attempt:
                self._sleep(self.retry_pause_s * attempt)
            started = time.monotonic()
            try:
                req = urllib.request.Request(self.url, data=data, headers=headers, method="POST")
                with self._opener(req, timeout=self.timeout_s) as resp:
                    payload = json.loads(resp.read().decode("utf-8"))
            except urllib.error.HTTPError as e:
                if e.code in (401, 403):
                    raise AuthError(f"HTTP {e.code}: check the API key") from None
                if e.code < 500 and e.code != 429:
                    raise ProviderError(f"HTTP {e.code}") from None
                last = f"HTTP {e.code}"
                continue
            except (urllib.error.URLError, socket.timeout, TimeoutError, ConnectionError, OSError) as e:
                last = f"{type(e).__name__}: {getattr(e, 'reason', e)}"
                continue
            except json.JSONDecodeError:
                last = "response is not JSON"
                continue
            try:
                choice = payload["choices"][0]
                content = choice["message"]["content"] or ""
            except (KeyError, IndexError, TypeError):
                raise ProviderError("malformed response: no choices[0].message.content") from None
            return Completion(content=content, usage=payload.get("usage") or {},
                              timings=payload.get("timings") or {},
                              elapsed_s=round(time.monotonic() - started, 1))
        raise ProviderUnavailable(f"after {self.retries + 1} attempts: {last}")
