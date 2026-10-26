#!/usr/bin/env python3
"""Export Claude dialogs to an Obsidian vault as Markdown notes.

Sources:
  * Claude Code transcripts (~/.claude/projects/<project>/<session>.jsonl),
    exported automatically by the plugin's Stop/SessionEnd hooks;
  * claude.ai data export (conversations.json or the export .zip).

Only the Python standard library is used. Run `obsidian_export.py --help`.
"""
from __future__ import annotations

import argparse
import datetime as dt
import fnmatch
import io
import json
import os
import re
import sys
import tempfile
import traceback
import urllib.parse
import zipfile
from pathlib import Path

VERSION = "1.0.0"

# User notes between these markers survive re-export. The start marker
# carries a localized hint: "%% obsidian-export:notes-start — ... %%".
NOTES_START = "%% obsidian-export:notes-start"
NOTES_END = "%% obsidian-export:notes-end %%"
NOTES_RE = re.compile(re.escape(NOTES_START) + r"[^\n]*?%%\n?(.*?)\n?" + re.escape(NOTES_END), re.S)

DEFAULTS = {
    "vault_path": "",
    "folder": "Claude",
    "filename_template": "{date} {title}",
    "claude_ai_folder": "Claude/claude.ai",
    "language": "ru",
    "auto_export": True,
    "include_thinking": False,
    "include_tool_calls": True,
    "include_tool_results": True,
    "max_tool_chars": 1500,
    "tags": ["claude"],
    "project_tags": True,
    "exclude_projects": [],
    "min_user_messages": 1,
}

LABELS = {
    "ru": {
        "user": "Пользователь",
        "assistant": "Claude",
        "thinking": "Размышления",
        "result": "Результат",
        "error": "Ошибка",
        "summary": "Кратко",
        "output": "Вывод",
        "compact": "Сжатый контекст",
        "interrupted": "Прервано пользователем",
        "image": "изображение",
        "attachment": "вложение",
        "truncated": "… обрезано, ещё {n} симв.",
        "in_progress": "в работе",
        "untitled": "Диалог",
        "notes_hint": "заметки между этими маркерами сохраняются при обновлении",
    },
    "en": {
        "user": "User",
        "assistant": "Claude",
        "thinking": "Thinking",
        "result": "Result",
        "error": "Error",
        "summary": "Summary",
        "output": "Output",
        "compact": "Compacted context",
        "interrupted": "Interrupted by user",
        "image": "image",
        "attachment": "attachment",
        "truncated": "… truncated, {n} more chars",
        "in_progress": "in progress",
        "untitled": "Dialog",
        "notes_hint": "notes between these markers survive re-export",
    },
}


# --------------------------------------------------------------------------
# Paths, config, state
# --------------------------------------------------------------------------

def claude_home() -> Path:
    return Path(os.environ.get("CLAUDE_CONFIG_DIR") or Path.home() / ".claude").expanduser()


def data_dir() -> Path:
    return claude_home() / "obsidian-export"


def config_path() -> Path:
    env = os.environ.get("OBSIDIAN_EXPORT_CONFIG")
    return Path(env).expanduser() if env else data_dir() / "config.json"


def state_path() -> Path:
    return data_dir() / "state.json"


def log(msg: str) -> None:
    try:
        data_dir().mkdir(parents=True, exist_ok=True)
        p = data_dir() / "export.log"
        if p.exists() and p.stat().st_size > 512 * 1024:
            p.replace(p.with_suffix(".log.old"))
        with open(p, "a", encoding="utf-8") as f:
            f.write(f"{dt.datetime.now().isoformat(timespec='seconds')} {msg}\n")
    except OSError:
        pass


def read_json(path: Path, default):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return default


def write_atomic(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=str(path.parent), prefix=".tmp-", suffix=path.suffix)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        os.replace(tmp, path)
    except BaseException:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise


def load_config() -> dict:
    cfg = dict(DEFAULTS)
    stored = read_json(config_path(), {})
    if isinstance(stored, dict):
        cfg.update(stored)
    if os.environ.get("OBSIDIAN_VAULT_PATH"):
        cfg["vault_path"] = os.environ["OBSIDIAN_VAULT_PATH"]
    if os.environ.get("OBSIDIAN_EXPORT_FOLDER"):
        cfg["folder"] = os.environ["OBSIDIAN_EXPORT_FOLDER"]
    if cfg.get("language") not in LABELS:
        cfg["language"] = "ru"
    return cfg


def load_state() -> dict:
    st = read_json(state_path(), {})
    if not isinstance(st, dict) or not isinstance(st.get("sessions"), dict):
        st = {"sessions": {}}
    return st


def update_state(key: str, rec: dict) -> None:
    # Re-read right before writing: hooks and the skill may run concurrently.
    st = load_state()
    st["sessions"].setdefault(key, {}).update(rec)
    write_atomic(state_path(), json.dumps(st, ensure_ascii=False, indent=1))


# --------------------------------------------------------------------------
# Obsidian vault discovery
# --------------------------------------------------------------------------

def obsidian_vaults() -> list:
    """Vaults registered in the Obsidian app, open/most recent first."""
    home = Path.home()
    cands = []
    if sys.platform == "darwin":
        cands.append(home / "Library/Application Support/obsidian/obsidian.json")
    elif os.name == "nt":
        cands.append(Path(os.environ.get("APPDATA", home / "AppData/Roaming")) / "obsidian/obsidian.json")
    else:
        xdg = Path(os.environ.get("XDG_CONFIG_HOME") or home / ".config")
        cands += [
            xdg / "obsidian/obsidian.json",
            home / ".var/app/md.obsidian.Obsidian/config/obsidian/obsidian.json",
            home / "snap/obsidian/current/.config/obsidian/obsidian.json",
        ]
    vaults = []
    for c in cands:
        data = read_json(c, {})
        for v in (data.get("vaults") or {}).values() if isinstance(data, dict) else []:
            if isinstance(v, dict) and v.get("path"):
                vaults.append({"path": v["path"], "open": bool(v.get("open")), "ts": v.get("ts") or 0})
    vaults.sort(key=lambda v: (not v["open"], -v["ts"]))
    return vaults


def resolve_vault(cfg: dict):
    if cfg.get("vault_path"):
        p = Path(os.path.expandvars(cfg["vault_path"])).expanduser()
        return p if p.is_dir() else None
    for v in obsidian_vaults():
        p = Path(v["path"])
        if p.is_dir():
            return p
    return None


# --------------------------------------------------------------------------
# Small helpers
# --------------------------------------------------------------------------

ANSI_RE = re.compile(r"\x1b\[[0-9;?]*[A-Za-z]")


def parse_ts(s):
    if not s:
        return None
    try:
        if isinstance(s, (int, float)):
            return dt.datetime.fromtimestamp(s / 1000 if s > 1e11 else s).astimezone()
        s = str(s).replace("Z", "+00:00")
        s = re.sub(r"(\.\d{6})\d+", r"\1", s)  # older Pythons accept <=6 digits
        d = dt.datetime.fromisoformat(s)
        if d.tzinfo is None:
            d = d.replace(tzinfo=dt.timezone.utc)
        return d.astimezone()
    except (ValueError, OverflowError, OSError):
        return None


def one_line(s: str, n: int) -> str:
    s = re.sub(r"\s+", " ", str(s)).strip()
    return s if len(s) <= n else s[: n - 1].rstrip() + "…"


def clip(s: str, n: int, L: dict) -> str:
    if n and len(s) > n:
        return s[:n].rstrip() + "\n" + L["truncated"].format(n=len(s) - n)
    return s


def fence(text: str, lang: str = "") -> str:
    longest = max([len(m) for m in re.findall(r"`{3,}", text)] + [2])
    f = "`" * (longest + 1)
    return f"{f}{lang}\n{text.rstrip()}\n{f}"


def quote(text: str) -> str:
    return "\n".join("> " + ln if ln else ">" for ln in text.split("\n"))


def callout(kind: str, title: str, body: str, folded: bool = True) -> str:
    head = f"> [!{kind}]{'-' if folded else ''} {title}"
    return head + ("\n" + quote(body) if body.strip() else "")


def inline_code(s: str) -> str:
    s = s.replace("`", "'")
    return f"`{s}`" if s else ""


def yaml_str(s) -> str:
    return json.dumps("" if s is None else str(s), ensure_ascii=False)


def tag_slug(s: str) -> str:
    s = re.sub(r"[^\w/-]+", "-", str(s).strip().lower(), flags=re.U).strip("-/")
    if s and s.replace("/", "").isdigit():
        s = "n" + s  # Obsidian tags must not be purely numeric
    return s


def safe_component(s: str) -> str:
    s = re.sub(r'[\\/:*?"<>|#^\[\]\x00-\x1f]', " ", s)
    s = re.sub(r"\s+", " ", s).strip(" .")
    return s[:100].rstrip(" .") or "dialog"


LANG_BY_EXT = {
    "py": "python", "js": "javascript", "ts": "typescript", "tsx": "tsx", "jsx": "jsx",
    "kt": "kotlin", "kts": "kotlin", "java": "java", "go": "go", "rs": "rust", "rb": "ruby",
    "sh": "bash", "md": "markdown", "json": "json", "yml": "yaml", "yaml": "yaml",
    "html": "html", "css": "css", "xml": "xml", "sql": "sql", "c": "c", "h": "c",
    "cpp": "cpp", "cs": "csharp", "swift": "swift", "php": "php", "toml": "toml",
}


def lang_for(path: str) -> str:
    return LANG_BY_EXT.get(path.rsplit(".", 1)[-1].lower(), "") if "." in path else ""


def flatten_content(c) -> str:
    """Text of a tool_result / message content (str or list of blocks)."""
    if c is None:
        return ""
    if isinstance(c, str):
        return c
    out = []
    for b in c if isinstance(c, list) else [c]:
        if isinstance(b, str):
            out.append(b)
        elif isinstance(b, dict):
            if b.get("type") == "text":
                out.append(b.get("text") or "")
            elif b.get("type") == "image":
                out.append("[image]")
            elif "text" in b:
                out.append(str(b["text"]))
    return "\n".join(out)


# --------------------------------------------------------------------------
# Parsing Claude Code transcripts
# --------------------------------------------------------------------------

def load_jsonl(path: Path) -> list:
    entries = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                e = json.loads(line)
            except ValueError:
                continue  # e.g. a line still being written
            if isinstance(e, dict):
                entries.append(e)
    return entries


def user_text_parts(s: str) -> list:
    s = re.sub(r"<system-reminder>.*?</system-reminder>", "", s, flags=re.S)
    parts = []
    m = re.search(r"<command-name>(.*?)</command-name>", s, re.S)
    if m:
        args = re.search(r"<command-args>(.*?)</command-args>", s, re.S)
        name = m.group(1).strip()
        cmd = name if name.startswith("/") else "/" + name
        parts.append(("command", (cmd + " " + (args.group(1).strip() if args else "")).strip()))
        return parts
    m = re.search(r"<bash-input>(.*?)</bash-input>", s, re.S)
    if m:
        return [("command", "! " + m.group(1).strip())]
    outs = re.findall(
        r"<(local-command-stdout|local-command-stderr|bash-stdout|bash-stderr)>(.*?)</\1>", s, re.S
    )
    if outs:
        text = "\n".join(ANSI_RE.sub("", o[1]).strip() for o in outs if o[1].strip())
        return [("stdout", text)] if text else []
    s = s.strip()
    if not s:
        return []
    if s.startswith("[Request interrupted by user"):
        return [("interrupted", "")]
    if s.startswith("Caveat: The messages below were generated by the user"):
        return []
    return [("text", s)]


def parse_user_content(content) -> list:
    if isinstance(content, str):
        return user_text_parts(content)
    parts = []
    for b in content or []:
        if isinstance(b, str):
            parts += user_text_parts(b)
        elif not isinstance(b, dict):
            continue
        elif b.get("type") == "text":
            parts += user_text_parts(b.get("text") or "")
        elif b.get("type") == "image":
            parts.append(("image", ""))
        elif b.get("type") == "document":
            parts.append(("attachment", (b.get("title") or (b.get("source") or {}).get("media_type") or "")))
    return parts


def parse_assistant_content(content, results: dict) -> list:
    if isinstance(content, str):
        return [("text", content)] if content.strip() else []
    parts = []
    for b in content or []:
        if not isinstance(b, dict):
            continue
        t = b.get("type")
        if t == "text" and (b.get("text") or "").strip():
            parts.append(("text", b["text"]))
        elif t == "thinking" and (b.get("thinking") or "").strip():
            parts.append(("thinking", b["thinking"]))
        elif t in ("tool_use", "server_tool_use", "mcp_tool_use"):
            parts.append(("tool", {
                "name": b.get("name") or "tool",
                "input": b.get("input"),
                "result": results.get(b.get("id")),
            }))
    return parts


def parse_transcript(entries: list) -> dict:
    results = {}
    for e in entries:
        if e.get("type") == "user":
            c = (e.get("message") or {}).get("content")
            for b in c if isinstance(c, list) else []:
                if isinstance(b, dict) and b.get("type") == "tool_result":
                    results[b.get("tool_use_id")] = (flatten_content(b.get("content")), bool(b.get("is_error")))

    meta = {"session_id": "", "cwd": "", "git_branch": "", "version": "", "model": "", "custom_title": ""}
    turns = []
    for e in entries:
        t = e.get("type")
        if t == "custom-title":
            meta["custom_title"] = e.get("customTitle") or e.get("title") or meta["custom_title"]
            continue
        if t not in ("user", "assistant") or e.get("isSidechain"):
            continue
        for src, dst in (("sessionId", "session_id"), ("cwd", "cwd")):
            if e.get(src) and not meta[dst]:
                meta[dst] = e[src]
        for src, dst in (("gitBranch", "git_branch"), ("version", "version")):
            if e.get(src):
                meta[dst] = e[src]  # keep the latest value
        msg = e.get("message") or {}
        ts = parse_ts(e.get("timestamp"))
        if t == "user":
            if e.get("isCompactSummary"):
                turns.append({"role": "system", "ts": ts, "parts": [("compact", flatten_content(msg.get("content")))]})
                continue
            if e.get("isMeta"):
                continue
            parts = parse_user_content(msg.get("content"))
            if parts:
                turns.append({"role": "user", "ts": ts, "end": ts, "parts": parts})
        else:
            model = msg.get("model")
            if model and not str(model).startswith("<"):
                meta["model"] = model
            parts = parse_assistant_content(msg.get("content"), results)
            if not parts:
                continue
            if turns and turns[-1]["role"] == "assistant":
                turns[-1]["parts"] += parts
                turns[-1]["end"] = ts or turns[-1]["end"]
            else:
                turns.append({"role": "assistant", "ts": ts, "end": ts, "parts": parts})
    return {"meta": meta, "turns": turns, "source": "claude-code"}


# --------------------------------------------------------------------------
# Parsing claude.ai export (conversations.json)
# --------------------------------------------------------------------------

def parse_claude_ai_conversation(conv: dict) -> dict:
    turns = []
    for m in conv.get("chat_messages") or []:
        role = "user" if m.get("sender") == "human" else "assistant"
        ts = parse_ts(m.get("created_at"))
        parts = []
        content = m.get("content")
        if isinstance(content, list) and content:
            for b in content:
                if not isinstance(b, dict):
                    continue
                if b.get("type") == "text" and (b.get("text") or "").strip():
                    parts.append(("text", b["text"]))
                elif b.get("type") == "thinking" and (b.get("thinking") or "").strip():
                    parts.append(("thinking", b["thinking"]))
                elif b.get("type") == "tool_use":
                    parts.append(("tool", {"name": b.get("name") or "tool", "input": b.get("input"), "result": None}))
        elif (m.get("text") or "").strip():
            parts.append(("text", m["text"]))
        for a in (m.get("attachments") or []) + (m.get("files") or []):
            if isinstance(a, dict):
                parts.append(("attachment", a.get("file_name") or a.get("file_uuid") or ""))
        if parts:
            turns.append({"role": role, "ts": ts, "end": ts, "parts": parts})
    meta = {
        "session_id": conv.get("uuid") or "",
        "cwd": "", "git_branch": "", "version": "", "model": conv.get("model") or "",
        "custom_title": conv.get("name") or "",
    }
    return {"meta": meta, "turns": turns, "source": "claude.ai"}


# --------------------------------------------------------------------------
# Rendering
# --------------------------------------------------------------------------

def derive_title(d: dict, L: dict) -> str:
    if d["meta"].get("custom_title"):
        return one_line(d["meta"]["custom_title"], 80)
    for t in d["turns"]:
        if t["role"] != "user":
            continue
        for kind, val in t["parts"]:
            if kind in ("text", "command") and val.strip():
                first = next((ln for ln in val.strip().splitlines() if ln.strip()), "")
                first = re.sub(r"[#>*_`\[\]]+", " ", first)
                title = one_line(first, 70)
                if title:
                    return title
    return f"{L['untitled']} {d['meta'].get('session_id', '')[:8]}".strip()


def tool_title(name: str, inp) -> str:
    if not isinstance(inp, dict):
        return ""
    if name == "Bash" and inp.get("description"):
        return one_line(inp["description"], 90)  # the command itself goes into the body
    for key in ("command", "file_path", "notebook_path", "path", "pattern", "url", "query",
                "description", "skill", "prompt", "subject"):
        if inp.get(key):
            return one_line(inp[key], 90)
    return ""


def render_tool(tool: dict, cfg: dict, L: dict) -> str:
    name, inp, n = tool["name"], tool["input"], int(cfg.get("max_tool_chars") or 0)
    body = []
    if isinstance(inp, dict) and name == "Bash" and inp.get("command"):
        body.append(fence(clip(inp["command"], n, L), "bash"))
    elif isinstance(inp, dict) and name in ("Edit", "MultiEdit") and inp.get("file_path"):
        edits = inp.get("edits") if name == "MultiEdit" else [inp]
        diff = []
        for ed in edits or []:
            if not isinstance(ed, dict):
                continue
            diff += ["- " + ln for ln in str(ed.get("old_string", "")).split("\n")]
            diff += ["+ " + ln for ln in str(ed.get("new_string", "")).split("\n")]
        body.append(fence(clip("\n".join(diff), n, L), "diff"))
    elif isinstance(inp, dict) and name == "Write" and inp.get("file_path"):
        body.append(fence(clip(str(inp.get("content", "")), n, L), lang_for(inp["file_path"])))
    elif isinstance(inp, dict) and name == "TodoWrite" and isinstance(inp.get("todos"), list):
        for td in inp["todos"]:
            if isinstance(td, dict):
                st = td.get("status")
                mark = "x" if st == "completed" else " "
                extra = f" *({L['in_progress']})*" if st == "in_progress" else ""
                body.append(f"- [{mark}] {td.get('content', '')}{extra}")
    elif isinstance(inp, dict) and name in ("Read", "Glob", "Grep", "LS") and len(inp) <= 2:
        pass  # the title already says everything
    elif inp not in (None, {}, ""):
        body.append(fence(clip(json.dumps(inp, ensure_ascii=False, indent=2), n, L), "json"))

    res = tool.get("result")
    if cfg.get("include_tool_results") and res and res[0].strip():
        text, is_err = res
        body.append(f"**{L['error'] if is_err else L['result']}:**")
        body.append(fence(clip(ANSI_RE.sub("", text).strip(), n, L)))

    title = f"🔧 {name}"
    summary = tool_title(name, inp)
    if summary:
        title += " · " + inline_code(summary)
    kind = "failure" if res and res[1] else "example"
    return callout(kind, title, "\n\n".join(body))


def fmt_time(ts, start) -> str:
    if not ts:
        return ""
    if start and ts.date() == start.date():
        return ts.strftime("%H:%M")
    return ts.strftime("%Y-%m-%d %H:%M")


def render_turn(turn: dict, cfg: dict, L: dict, start) -> str:
    out = []
    if turn["role"] == "system":
        for _, text in turn["parts"]:
            out.append(callout("abstract", "🗜 " + L["compact"], text.strip()))
        return "\n\n".join(out)
    icon, who = ("👤", L["user"]) if turn["role"] == "user" else ("🤖", L["assistant"])
    when = fmt_time(turn.get("ts"), start)
    out.append(f"## {icon} {who}" + (f" · {when}" if when else ""))
    for kind, val in turn["parts"]:
        if kind == "text":
            out.append(val.strip())
        elif kind == "command":
            out.append(inline_code(val))
        elif kind == "stdout":
            out.append(callout("note", L["output"], fence(clip(val, int(cfg.get("max_tool_chars") or 0), L))))
        elif kind == "interrupted":
            out.append(f"*⏹ {L['interrupted']}*")
        elif kind == "image":
            out.append(f"🖼 *[{L['image']}]*")
        elif kind == "attachment":
            out.append(f"📎 *[{L['attachment']}{': ' + val if val else ''}]*")
        elif kind == "thinking" and cfg.get("include_thinking"):
            out.append(callout("quote", "💭 " + L["thinking"], val.strip()))
        elif kind == "tool" and cfg.get("include_tool_calls"):
            out.append(render_tool(val, cfg, L))
    if len(out) == 1:
        return ""  # only tool calls/thinking that are hidden by config
    return "\n\n".join(out)


def render_note(d: dict, cfg: dict, extra: dict, notes: str) -> str:
    L = LABELS[cfg["language"]]
    m = d["meta"]
    stamps = [t["ts"] for t in d["turns"] if t.get("ts")] + [t["end"] for t in d["turns"] if t.get("end")]
    start = min(stamps) if stamps else None
    end = max(stamps) if stamps else None
    title = extra.get("title") or derive_title(d, L)
    project = Path(m["cwd"]).name if m.get("cwd") else ""

    tags = []
    for tg in list(cfg.get("tags") or []) + list(extra.get("tags") or []):
        s = tag_slug(tg)
        if s and s not in tags:
            tags.append(s)
    if cfg.get("project_tags") and project:
        s = tag_slug("claude/project/" + project)
        if s not in tags:
            tags.append(s)

    fm = ["---", f"title: {yaml_str(title)}"]
    if extra.get("summary"):
        fm.append(f"summary: {yaml_str(one_line(extra['summary'], 400))}")
    fm.append(f"session_id: {yaml_str(m.get('session_id'))}")
    fm.append(f"source: {d['source']}")
    for key, val in (("project", project), ("cwd", m.get("cwd")), ("git_branch", m.get("git_branch")),
                     ("model", m.get("model")), ("claude_code_version", m.get("version"))):
        if val:
            fm.append(f"{key}: {yaml_str(val)}")
    if start:
        fm.append(f"created: {start.strftime('%Y-%m-%dT%H:%M:%S')}")
    if end:
        fm.append(f"updated: {end.strftime('%Y-%m-%dT%H:%M:%S')}")
    fm.append(f"user_messages: {sum(1 for t in d['turns'] if t['role'] == 'user')}")
    if tags:
        fm.append("tags:")
        fm += [f"  - {t}" for t in tags]
    fm.append("---")

    body = [f"# {title}"]
    if extra.get("summary"):
        body.append(callout("summary", L["summary"], extra["summary"].strip(), folded=False))
    body.append(f"{NOTES_START} — {L['notes_hint']} %%\n{notes}\n{NOTES_END}")
    for turn in d["turns"]:
        r = render_turn(turn, cfg, L, start)
        if r:
            body.append(r)
    return "\n".join(fm) + "\n\n" + "\n\n".join(body).rstrip() + "\n"


def extract_notes(path: Path) -> str:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError:
        return ""
    m = NOTES_RE.search(text)
    return m.group(1).strip("\n") if m else ""


# --------------------------------------------------------------------------
# Writing into the vault
# --------------------------------------------------------------------------

def find_note_by_session(root: Path, sid: str):
    if not sid or not root.is_dir():
        return None
    needle = f"session_id: {yaml_str(sid)}"
    for p in root.rglob("*.md"):
        try:
            with open(p, encoding="utf-8", errors="replace") as f:
                if needle in f.read(4096):
                    return p
        except OSError:
            continue
    return None


def new_note_path(vault: Path, folder: str, d: dict, title: str, cfg: dict) -> Path:
    m = d["meta"]
    stamps = [t["ts"] for t in d["turns"] if t.get("ts")]
    start = min(stamps) if stamps else dt.datetime.now().astimezone()
    values = {
        "date": start.strftime("%Y-%m-%d"),
        "time": start.strftime("%H-%M"),
        "title": title.rstrip("…").rstrip(),
        "project": Path(m["cwd"]).name if m.get("cwd") else "claude",
        "session": (m.get("session_id") or "")[:8],
    }
    tmpl = cfg.get("filename_template") or DEFAULTS["filename_template"]
    try:
        rel = tmpl.format(**values)
    except (KeyError, IndexError, ValueError):
        rel = DEFAULTS["filename_template"].format(**values)
    comps = [safe_component(c) for c in rel.replace("\\", "/").split("/") if c.strip()]
    base = vault.joinpath(*[c for c in folder.replace("\\", "/").split("/") if c], *comps)
    p = base.with_name(base.name + ".md")
    if p.exists():
        p = base.with_name(f"{base.name} ({values['session'] or 'copy'}).md")
    return p


def obsidian_uri(vault: Path, note: Path) -> str:
    rel = note.relative_to(vault).as_posix()
    if rel.endswith(".md"):
        rel = rel[:-3]
    q = urllib.parse.urlencode({"vault": vault.name, "file": rel}, quote_via=urllib.parse.quote)
    return "obsidian://open?" + q


def export_dialog(d: dict, cfg: dict, *, key: str = "", folder: str = "", summary=None, tags=None,
                  title=None, rename: bool = False, force: bool = False) -> dict:
    vault = resolve_vault(cfg)
    if not vault:
        raise RuntimeError("Obsidian vault not found: run `setup --vault /path/to/vault`")
    m = d["meta"]
    sid = m.get("session_id") or ""
    key = key or sid
    users = sum(1 for t in d["turns"] if t["role"] == "user")
    if not d["turns"]:
        return {"skipped": "empty dialog", "session_id": sid}
    if users < int(cfg.get("min_user_messages") or 0) and not force:
        return {"skipped": "too few user messages", "session_id": sid}

    rec = load_state()["sessions"].get(key, {})
    if summary is not None:
        rec["summary"] = summary.strip()
    if tags is not None:
        rec["tags"] = tags
    if title:
        rec["title"] = title.strip()
    extra = {"summary": rec.get("summary"), "tags": rec.get("tags") or [], "title": rec.get("title")}
    folder = folder or cfg.get("folder") or ""

    path = None
    if rec.get("path"):
        p = vault / rec["path"]
        path = p if p.exists() else None
    if path is None:
        # The note may have been renamed or moved inside the vault.
        path = (find_note_by_session(vault / folder, sid) if folder else None) or find_note_by_session(vault, sid)
    L = LABELS[cfg["language"]]
    final_title = extra["title"] or derive_title(d, L)
    if path is None:
        path = new_note_path(vault, folder, d, final_title, cfg)
    elif rename:
        target = new_note_path(vault, folder, d, final_title, cfg)
        if target != path:
            target.parent.mkdir(parents=True, exist_ok=True)
            os.replace(path, target)
            path = target

    notes = extract_notes(path) if path.exists() else ""
    text = render_note(d, cfg, extra, notes)
    changed = True
    if path.exists():
        try:
            changed = path.read_text(encoding="utf-8") != text
        except OSError:
            pass
    if changed:
        write_atomic(path, text)
    rel = path.relative_to(vault).as_posix()
    rec.update({"path": rel, "exported_at": dt.datetime.now().isoformat(timespec="seconds")})
    update_state(key, rec)
    return {"path": str(path), "vault": str(vault), "relative": rel, "uri": obsidian_uri(vault, path),
            "changed": changed, "session_id": sid, "title": final_title}


# --------------------------------------------------------------------------
# Locating transcripts
# --------------------------------------------------------------------------

def projects_dir() -> Path:
    return claude_home() / "projects"


def project_dir_for(cwd: str) -> Path:
    return projects_dir() / re.sub(r"[^A-Za-z0-9]", "-", str(Path(cwd).resolve()))


def transcript_files(root: Path) -> list:
    if not root.is_dir():
        return []
    return [p for p in root.glob("*.jsonl") if not p.name.startswith("agent-")]


def find_transcript(session_id: str = "", cwd: str = "") -> Path | None:
    if session_id and not session_id.startswith("${"):
        hits = list(projects_dir().glob(f"*/{glob_escape(session_id)}.jsonl"))
        if hits:
            return max(hits, key=lambda p: p.stat().st_mtime)
    files = transcript_files(project_dir_for(cwd or os.getcwd()))
    if not files:
        files = [p for d in projects_dir().glob("*") for p in transcript_files(d)]
    return max(files, key=lambda p: p.stat().st_mtime) if files else None


def glob_escape(s: str) -> str:
    return re.sub(r"([*?\[])", r"[\1]", s)


def is_excluded(d: dict, cfg: dict) -> bool:
    cwd = d["meta"].get("cwd") or ""
    name = Path(cwd).name if cwd else ""
    for pat in cfg.get("exclude_projects") or []:
        if fnmatch.fnmatch(name, pat) or fnmatch.fnmatch(cwd, pat) or (cwd and cwd.startswith(str(pat))):
            return True
    return False


def export_transcript(path: Path, cfg: dict, **kw) -> dict:
    d = parse_transcript(load_jsonl(path))
    if not d["meta"]["session_id"]:
        d["meta"]["session_id"] = path.stem
    if is_excluded(d, cfg) and not kw.get("force"):
        return {"skipped": "excluded project", "session_id": d["meta"]["session_id"]}
    return export_dialog(d, cfg, **kw)


# --------------------------------------------------------------------------
# Commands
# --------------------------------------------------------------------------

def out(obj) -> None:
    sys.stdout.write(json.dumps(obj, ensure_ascii=False, indent=2) + "\n")


def cmd_hook(args) -> int:
    """Called by Claude Code hooks. Never fails loudly and never blocks Claude."""
    try:
        raw = sys.stdin.read() if not sys.stdin.isatty() else ""
        payload = json.loads(raw) if raw.strip() else {}
        cfg = load_config()
        if not cfg.get("auto_export") or os.environ.get("OBSIDIAN_EXPORT_DISABLE") in ("1", "true", "yes"):
            return 0
        tp = payload.get("transcript_path")
        path = Path(tp).expanduser() if tp else find_transcript(payload.get("session_id", ""), payload.get("cwd", ""))
        if not path or not path.exists():
            log(f"hook: transcript not found ({payload.get('session_id')})")
            return 0
        if not resolve_vault(cfg):
            log("hook: vault not configured or missing; run setup")
            return 0
        res = export_transcript(path, cfg)
        if res.get("changed"):
            log(f"{payload.get('hook_event_name', 'hook')}: {res.get('relative')}")
    except Exception:  # noqa: BLE001 - a hook must never break the session
        log("hook error:\n" + traceback.format_exc())
    return 0


def read_text_arg(value, file_arg):
    if file_arg:
        return sys.stdin.read() if file_arg == "-" else Path(file_arg).expanduser().read_text(encoding="utf-8")
    return value


def cmd_export(args) -> int:
    cfg = load_config()
    if args.transcript:
        path = Path(args.transcript).expanduser()
    else:
        path = find_transcript(args.session or "", args.cwd or "")
    if not path or not path.exists():
        out({"error": "transcript not found", "session": args.session})
        return 1
    tags = [t.strip() for t in args.tags.split(",") if t.strip()] if args.tags is not None else None
    try:
        res = export_transcript(path, cfg, summary=read_text_arg(args.summary, args.summary_file), tags=tags,
                                title=args.title, rename=args.rename, force=True)
    except RuntimeError as e:
        out({"error": str(e), "detected_vaults": obsidian_vaults()})
        return 1
    res["transcript"] = str(path)
    out(res)
    return 0


def cmd_backfill(args) -> int:
    cfg = load_config()
    if not resolve_vault(cfg):
        out({"error": "Obsidian vault not found: run `setup --vault /path/to/vault`"})
        return 1
    dirs = [d for d in projects_dir().glob("*") if d.is_dir()] if args.all else [project_dir_for(args.cwd or os.getcwd())]
    done, skipped, failed = [], 0, []
    for d in dirs:
        for p in sorted(transcript_files(d), key=lambda p: p.stat().st_mtime):
            try:
                res = export_transcript(p, cfg)
                if res.get("skipped"):
                    skipped += 1
                else:
                    done.append(res["relative"])
            except Exception as e:  # noqa: BLE001
                failed.append(f"{p.name}: {e}")
    out({"exported": len(done), "skipped": skipped, "failed": failed, "notes": done[-20:]})
    return 0


def iter_claude_ai_conversations(src: Path):
    if zipfile.is_zipfile(src):
        with zipfile.ZipFile(src) as z:
            name = next((n for n in z.namelist() if n.endswith("conversations.json")), None)
            if not name:
                raise RuntimeError("conversations.json not found in the archive")
            data = json.load(io.TextIOWrapper(z.open(name), encoding="utf-8"))
    else:
        with open(src, encoding="utf-8") as f:
            data = json.load(f)
    return data if isinstance(data, list) else data.get("conversations", [])


def cmd_import_claude_ai(args) -> int:
    cfg = load_config()
    if not resolve_vault(cfg):
        out({"error": "Obsidian vault not found: run `setup --vault /path/to/vault`"})
        return 1
    folder = args.folder or cfg.get("claude_ai_folder") or "Claude/claude.ai"
    cfg = dict(cfg, project_tags=False)
    n, skipped, failed = 0, 0, []
    for conv in iter_claude_ai_conversations(Path(args.path).expanduser()):
        try:
            d = parse_claude_ai_conversation(conv)
            res = export_dialog(d, cfg, key="claude.ai:" + d["meta"]["session_id"], folder=folder,
                                tags=["claude/claude-ai"])
            if res.get("skipped"):
                skipped += 1
            else:
                n += 1
        except Exception as e:  # noqa: BLE001
            failed.append(f"{conv.get('name') or conv.get('uuid')}: {e}")
    out({"imported": n, "skipped": skipped, "failed": failed, "folder": folder})
    return 0


def parse_value(v: str):
    try:
        return json.loads(v)
    except ValueError:
        return v


def cmd_setup(args) -> int:
    stored = read_json(config_path(), {})
    stored = stored if isinstance(stored, dict) else {}
    if args.vault:
        p = Path(os.path.expandvars(args.vault)).expanduser()
        if not p.is_dir():
            out({"error": f"directory does not exist: {p}", "detected_vaults": obsidian_vaults()})
            return 1
        stored["vault_path"] = str(p.resolve())
    if args.folder is not None:
        stored["folder"] = args.folder.strip("/\\")
    if args.language:
        stored["language"] = args.language
    for kv in args.set or []:
        if "=" not in kv:
            out({"error": f"expected key=value, got {kv!r}"})
            return 1
        k, v = kv.split("=", 1)
        if k not in DEFAULTS:
            out({"error": f"unknown option {k!r}", "options": sorted(DEFAULTS)})
            return 1
        stored[k] = parse_value(v)
    if args.vault or args.folder is not None or args.language or args.set:
        write_atomic(config_path(), json.dumps(stored, ensure_ascii=False, indent=2) + "\n")
    return cmd_status(args)


def cmd_status(args) -> int:
    cfg = load_config()
    vault = resolve_vault(cfg)
    st = load_state()
    logf = data_dir() / "export.log"
    tail = []
    if logf.exists():
        tail = logf.read_text(encoding="utf-8", errors="replace").splitlines()[-5:]
    out({
        "version": VERSION,
        "config_file": str(config_path()),
        "config": cfg,
        "vault_resolved": str(vault) if vault else None,
        "vault_source": "config" if cfg.get("vault_path") else ("obsidian.json" if vault else None),
        "detected_vaults": obsidian_vaults(),
        "exported_sessions": len(st["sessions"]),
        "log_tail": tail,
    })
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Export Claude dialogs to Obsidian as Markdown")
    ap.add_argument("--version", action="version", version=VERSION)
    sub = ap.add_subparsers(dest="cmd", required=True)

    sub.add_parser("hook", help="read a Claude Code hook payload from stdin and export that session")

    e = sub.add_parser("export", help="export one Claude Code session now")
    e.add_argument("--session", help="session id (default: newest session of --cwd)")
    e.add_argument("--transcript", help="path to a .jsonl transcript")
    e.add_argument("--cwd", help="project directory used to find the newest session")
    e.add_argument("--summary", help="summary shown at the top of the note")
    e.add_argument("--summary-file", help="read the summary from a file ('-' = stdin)")
    e.add_argument("--tags", help="comma-separated extra tags (replaces previously set extra tags)")
    e.add_argument("--title", help="note title")
    e.add_argument("--rename", action="store_true", help="also rename the file to match the title")

    b = sub.add_parser("backfill", help="export all past sessions of a project")
    b.add_argument("--cwd", help="project directory (default: current)")
    b.add_argument("--all", action="store_true", help="every project in ~/.claude/projects")

    i = sub.add_parser("import-claude-ai", help="import a claude.ai data export (.zip or conversations.json)")
    i.add_argument("path")
    i.add_argument("--folder", help="vault folder (default: config claude_ai_folder)")

    s = sub.add_parser("setup", help="configure the vault and options, then print status")
    s.add_argument("--vault", help="path to the Obsidian vault")
    s.add_argument("--folder", help="folder inside the vault")
    s.add_argument("--language", choices=sorted(LABELS))
    s.add_argument("--set", action="append", metavar="KEY=VALUE", help="any option, value parsed as JSON")

    sub.add_parser("status", help="print configuration and detected vaults")

    args = ap.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(encoding="utf-8")
        except (OSError, ValueError):
            pass
    return {
        "hook": cmd_hook, "export": cmd_export, "backfill": cmd_backfill,
        "import-claude-ai": cmd_import_claude_ai, "setup": cmd_setup, "status": cmd_status,
    }[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
