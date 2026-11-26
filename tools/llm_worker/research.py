"""research: DeepSeek checks claims and collects facts from pages the tool fetched itself (#577).

The DeepSeek API has no network access, so the tool downloads the pages (curl: http/https only, size limit,
cache outside the repository, no cookies), reduces them to text, scans the text with gitleaks and gives it to
the model. The model's quote is searched in the page text: a quote that is not there turns the verdict into
not_found. Page text is never stored in the run record (only url, hash and length)."""

import hashlib
import html.parser
import ipaddress
import json
import re
import socket
import subprocess
import sys
import threading
import urllib.parse
from datetime import datetime, timezone
from pathlib import Path

from . import config, gitleaks, jsonschema_lite, review
from .review import ReviewError

SCHEMA = config.PACKAGE_DIR / "schemas" / "research.schema.json"
CACHE_DIR = Path.home() / ".cache" / "singularity" / "research"
MAX_PAGE_BYTES = 3_000_000
MAX_PAGE_CHARS = 24_000  # per page, what the model sees
MAX_URLS = 4
MAX_QUOTE_WORDS = 30
MIN_QUOTE_WORDS = 6
MIN_QUOTE_CHARS = 30
MAX_REDIRECTS = 5
FETCH_TIMEOUT_S = 30
VERDICTS = ("confirmed", "contradicted", "not_found")
SYSTEM = (
    "You check claims and collect facts using ONLY the page texts given by the user. You have no other "
    "knowledge to use. For the item, answer with one json object: verdict (confirmed: a page states the claim; "
    "contradicted: a page states something different; not_found: the pages say nothing about it), source (the "
    "number of the page that holds the quote), quote (an exact copy of at most 30 words from that page, "
    "empty for not_found), value (the correct value as the page gives it, for example a year; empty if none), "
    "note (one short sentence). For a question item, confirmed means the answer is on a page and goes into "
    "value. Never invent a quote: it is searched in the page text.")


class FetchError(RuntimeError):
    pass


def check_url(url):
    """Only http/https with a public host. Returns (host, port, ip): the ip that was checked, to pin in curl."""
    u = urllib.parse.urlparse(url)
    if u.scheme not in ("http", "https") or not u.hostname or u.username or u.password:
        raise FetchError(f"refused url (http/https without credentials only): {url[:80]}")
    try:
        port = u.port or (443 if u.scheme == "https" else 80)
        infos = socket.getaddrinfo(u.hostname, port, type=socket.SOCK_STREAM)
    except (OSError, ValueError):
        raise FetchError(f"cannot resolve {u.hostname}") from None
    ips = [ipaddress.ip_address(i[4][0]) for i in infos]
    if not ips or not all(ip.is_global for ip in ips):
        raise FetchError(f"refused non-public address for {u.hostname}")
    return u.hostname, port, str(ips[0])


def _run_capped(cmd, timeout_s, cap):
    """Run cmd; stdout is read in chunks and the process is killed past `cap` bytes: curl's own --max-filesize
    does nothing when the size is not announced. Returns a CompletedProcess."""
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    timed_out = threading.Event()
    timer = threading.Timer(timeout_s, lambda: (timed_out.set(), proc.kill()))
    timer.start()
    chunks, size = [], 0
    try:
        while True:
            chunk = proc.stdout.read(65536)
            if not chunk:
                break
            chunks.append(chunk)
            size += len(chunk)
            if size > cap:
                proc.kill()
                raise FetchError(f"response larger than {cap} bytes")
        err = proc.stderr.read(4096)
        rc = proc.wait()
    finally:
        timer.cancel()
        proc.kill()
        proc.stdout.close()
        proc.stderr.close()
        proc.wait()
    if timed_out.is_set():
        raise subprocess.TimeoutExpired(cmd, timeout_s)
    return subprocess.CompletedProcess(cmd, rc, stdout=b"".join(chunks), stderr=err)


def curl_fetch(url, timeout_s=FETCH_TIMEOUT_S, max_hops=MAX_REDIRECTS):
    """Body bytes via curl. No cookies, no config file, no proxy. Redirects are followed here, one hop at a time:
    every hop is checked and its address pinned with --resolve, so the checked address is the one curl uses."""
    for _ in range(max_hops + 1):
        host, port, ip = check_url(url)
        pin = f"[{ip}]" if ":" in ip else ip
        cmd = ["curl", "-q", "-sS", "--noproxy", "*", "--resolve", f"{host}:{port}:{pin}",
               "--proto", "=http,https", "--max-filesize", str(MAX_PAGE_BYTES), "--max-time", str(timeout_s),
               "-A", "singularity-research/1", "-w", "\n%{http_code}\n%{redirect_url}\n%{remote_ip}", "--", url]
        try:
            p = _run_capped(cmd, timeout_s + 5, MAX_PAGE_BYTES + 1024)
        except (OSError, subprocess.TimeoutExpired) as e:
            raise FetchError(f"curl failed: {type(e).__name__}") from None
        if p.returncode != 0:
            raise FetchError(f"curl rc {p.returncode}: {p.stderr.decode('utf-8', 'replace').strip()[:120]}")
        parts = p.stdout.rsplit(b"\n", 3)
        if len(parts) != 4:
            raise FetchError("curl gave no status line")
        body, code, redirect, remote = parts
        try:
            if str(ipaddress.ip_address(remote.decode().strip())) != ip:
                raise FetchError("refused: curl connected to an address that was not checked")
        except ValueError:
            raise FetchError("refused: no usable remote address") from None  # fail closed
        status = code.decode().strip()
        if status.startswith("3") and redirect:
            url = redirect.decode("utf-8", "replace").strip()
            continue
        if not status.startswith("2"):
            raise FetchError(f"HTTP {status}")
        return body
    raise FetchError("too many redirects")


class _Text(html.parser.HTMLParser):
    SKIP = {"script", "style", "noscript", "svg", "head", "nav", "footer"}

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.parts, self.skip = [], 0

    def handle_starttag(self, tag, attrs):
        if tag in self.SKIP:
            self.skip += 1
        elif tag in ("p", "br", "li", "tr", "h1", "h2", "h3", "h4", "div"):
            self.parts.append("\n")

    def handle_endtag(self, tag):
        if tag in self.SKIP and self.skip:
            self.skip -= 1

    def handle_data(self, data):
        if not self.skip:
            self.parts.append(data)


def extract_text(raw):
    """Page bytes -> plain text (HTML tags dropped; plain text stays as it is)."""
    s = raw.decode("utf-8", "replace")
    if "<" in s[:2000] and re.search(r"<(html|body|p|div|head)\b", s[:5000], re.I):
        p = _Text()
        p.feed(s)
        s = "".join(p.parts)
    return re.sub(r"[ \t\r\f\v]+", " ", re.sub(r"\n\s*\n+", "\n", s)).strip()


def _norm(s):
    s = s.translate({0x2018: "'", 0x2019: "'", 0x201C: '"', 0x201D: '"', 0x2013: "-", 0x2014: "-", 0xA0: " ", 0x2010: "-", 0x2011: "-", 0xAD: None, 0x2026: "..."})
    return re.sub(r"\s+", " ", s).strip().lower()


def has_token(needle, text):
    """needle occurs in text and is not glued to other letters or digits ('955' is not in '1955')."""
    return re.search(r"(?<!\w)" + re.escape(needle) + r"(?!\w)", text) is not None


def quote_in_text(quote, text, value=""):
    """True when the quote occurs in the page text (case, spaces and typographic marks ignored), has 6 to 30
    words and at least 30 characters, and contains the stated value when there is one."""
    q = _norm(quote)
    if len(q) < MIN_QUOTE_CHARS or not MIN_QUOTE_WORDS <= len(q.split()) <= MAX_QUOTE_WORDS:
        return False
    if _norm(value) and not has_token(_norm(value), q):
        return False
    return q in _norm(text)


def load_input(path):
    import yaml
    data = yaml.safe_load(Path(path).read_text(encoding="utf-8"))
    items = data.get("items") if isinstance(data, dict) else None
    if not isinstance(items, list) or not items:
        raise ReviewError("bad_input", "the YAML needs a non-empty 'items' list")
    seen = set()
    for it in items:
        if not isinstance(it, dict) or not isinstance(it.get("id"), str) or it["id"] in seen:
            raise ReviewError("bad_input", f"item without a unique string id: {str(it)[:60]}")
        seen.add(it["id"])
        if not (isinstance(it.get("claim"), str) or isinstance(it.get("question"), str)):
            raise ReviewError("bad_input", f"{it['id']}: needs 'claim' or 'question'")
        urls = it.get("urls")
        if not isinstance(urls, list) or not urls or not all(isinstance(u, str) for u in urls):
            raise ReviewError("bad_input", f"{it['id']}: needs a list 'urls'")
    return data


def load_page(url, fetch, cache_dir):
    """(text or None, info). The cache holds raw bytes by url hash; gitleaks scans the text before it can be used."""
    info = {"url": url}
    key = hashlib.sha256(url.encode()).hexdigest()
    cached = cache_dir / key
    try:
        if cached.is_file():
            raw = cached.read_bytes()
        else:
            raw = fetch(url)
            cache_dir.mkdir(parents=True, exist_ok=True)
            cached.write_bytes(raw)
    except FetchError as e:
        return None, {**info, "error": str(e)}
    text = extract_text(raw)[:MAX_PAGE_CHARS]
    info.update(sha256=hashlib.sha256(raw).hexdigest(), chars=len(text))
    if not text:
        return None, {**info, "error": "empty_text"}
    try:
        gitleaks.scan_text(text)
    except gitleaks.SecretDetected as e:
        return None, {**info, "error": f"secret_detected: {e}"}
    except gitleaks.GitleaksUnavailable as e:
        raise ReviewError("sandbox_unavailable", str(e)) from None
    return text, info


def build_prompt(item, pages):
    head = f"Claim: {item['claim']}" if "claim" in item else f"Question: {item['question']}"
    for k in ("field", "year", "value"):
        if k in item:
            head += f"\n{k}: {item[k]}"
    body = "\n".join(f"=== PAGE {i} ({u}) ===\n{t}\n=== END PAGE {i} ===" for i, (u, t) in enumerate(pages))
    return f"{head}\n\n{body}"


def judge(provider, item, pages):
    """One model call; returns the checked result dict. A quote not found in the source page gives not_found."""
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    messages = [{"role": "system", "content": SYSTEM}, {"role": "user", "content": build_prompt(item, pages)}]
    comp = provider.complete(messages, schema, "research", 1500)
    data = review._loads(comp.content)
    errors = ["not a JSON object"] if not isinstance(data, dict) else jsonschema_lite.validate(data, schema)
    if errors or data["source"] >= len(pages):
        return {"verdict": "not_found", "source": None, "quote": "", "value": "", "usage": comp.usage,
                "note": "schema_invalid: " + ("; ".join(errors[:3]) or "source out of range")}
    res = {k: data[k] for k in ("verdict", "source", "quote", "value", "note")}
    res["usage"] = comp.usage
    said = res["verdict"]
    if said in ("confirmed", "contradicted"):
        reason = None
        if not quote_in_text(res["quote"], pages[res["source"]][1], res["value"]):
            reason = "quote_not_in_page"
        elif "claim" not in item and not res["value"].strip():
            reason = "no_value_for_question"
        elif said == "contradicted" and "year" in item and has_token(_norm(str(item["year"])), _norm(res["value"])):
            reason = "contradiction_gives_the_submitted_year"
        res["quote_verified"] = reason != "quote_not_in_page"
        if reason:
            # Nothing the model claimed survives into the artifacts: the rejected quote, value and source are dropped.
            res.update(verdict="not_found", source=None, quote="", value="", note=f"{reason} (model said {said})")
    res["source_url"] = pages[res["source"]][0] if res["verdict"] != "not_found" else None
    return res


def run_research(path, runs_dir=config.RUNS_DIR, make_provider=None, fetch=curl_fetch, cache_dir=CACHE_DIR,
                 profile_name="deepseek-flash", dry_run=False, now=None):
    """Returns (record path or None, record, markdown)."""
    data = load_input(path)
    stem = "research-" + Path(path).stem
    key = "research:" + Path(path).stem
    record = {"kind": "research", "input": str(path), "provider": profile_name, "items": []}
    if not dry_run:
        make_provider = make_provider or (lambda: review.paid_provider(profile_name, key))
        provider = make_provider()
    def save(error=None):
        record["created"] = (now or datetime.now(timezone.utc)).strftime("%Y-%m-%dT%H:%M:%SZ")
        record["complete"] = error is None
        if error:
            record["error"] = error
        digest = hashlib.sha256(json.dumps(record["items"], sort_keys=True).encode()).hexdigest()[:8]
        path_, sha = review.write_run(runs_dir, f"{stem}-{digest}.json", record, unique=True)
        path_.with_suffix(".md").write_text(markdown(record), encoding="utf-8")
        record["sha256"] = sha
        return path_

    for it in data["items"]:
        out = {"id": it["id"], "text": it.get("claim") or it.get("question"), "pages": []}
        pages = []
        for url in it["urls"][:MAX_URLS]:
            text, info = load_page(url, fetch, cache_dir)
            out["pages"].append(info)
            if text:
                pages.append((url, text))
        if dry_run or not pages:
            out.update(verdict="not_found", note="dry_run" if dry_run else "no_page_available")
        else:
            try:
                out.update(judge(provider, it, pages))
            except Exception as e:
                # Paid answers already received must not be lost: keep them in a partial record, then fail.
                if record["items"]:
                    saved = save(f"{type(e).__name__}: {e}")
                    print(f"partial results saved: {saved}", file=sys.stderr)
                raise
        record["items"].append(out)
    if dry_run:
        record["created"] = (now or datetime.now(timezone.utc)).strftime("%Y-%m-%dT%H:%M:%SZ")
        return None, record, markdown(record)
    return save(), record, markdown(record)


def _cell(s):
    return str(s or "").replace("|", "/").replace("\n", " ")


def markdown(record):
    rows = ["| id | verdict | value | quote | source | note |", "|---|---|---|---|---|---|"]
    for it in record["items"]:
        rows.append(f"| {_cell(it['id'])} | {it['verdict']} | {_cell(it.get('value'))} | {_cell(it.get('quote'))} "
                    f"| {_cell(it.get('source_url'))} | {_cell(it.get('note'))} |")
    return "\n".join(rows) + "\n"
