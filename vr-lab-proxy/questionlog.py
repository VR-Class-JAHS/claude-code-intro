"""
Optional log of what students asked and what Claude answered, for the "note what confused them" step of a
classroom trial. Off unless LOG_QUESTIONS=1 is in .env.

One JSON line per event in logs/YYYY-MM-DD.jsonl next to this file (gitignored):
  {"time", "headset": the requesting device's address, "question": the student's words,
   "lab_state": the snapshot of the lab the headset attached (if any)}                    when a new question arrives
  {"time", "headset", "reply": Claude's text, "tools": [tools used this round], "tokens": {...}}  for each answer round
Only text is logged: no audio, no names. The headset address identifies a device, not a student.
"""

import json
import os
import threading
from datetime import datetime
from pathlib import Path

ENABLED = os.environ.get("LOG_QUESTIONS", "0") == "1"
LOG_DIR = Path(__file__).parent / "logs"
_lock = threading.Lock()


def _write(entry: dict) -> None:
    entry = {"time": datetime.now().isoformat(timespec="seconds"), **entry}
    LOG_DIR.mkdir(exist_ok=True)
    path = LOG_DIR / f"{datetime.now():%Y-%m-%d}.jsonl"
    with _lock, open(path, "a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")


def log_request(headset: str, messages: list[dict]) -> None:
    """Log the student's question when the last message is a new question (plain text, not tool results)."""
    if not ENABLED or not messages:
        return
    last = messages[-1]
    if last.get("role") != "user":
        return
    content = last.get("content")
    lab_state = None
    if isinstance(content, str):
        question = content
    elif isinstance(content, list) and all(isinstance(b, dict) and b.get("type") == "text" for b in content):
        # The headset adds a <lab_state> snapshot block after the student's words; keep it apart from the question.
        texts = [b.get("text", "") for b in content]
        question = " ".join(t for t in texts if not t.startswith("<lab_state>"))
        states = [t for t in texts if t.startswith("<lab_state>")]
        if states:
            lab_state = states[-1].removeprefix("<lab_state>").removesuffix("</lab_state>").strip()
    else:
        return  # tool results: part of an answer already in progress
    entry = {"headset": headset, "question": question}
    if lab_state:
        entry["lab_state"] = lab_state
    _write(entry)


def log_response(headset: str, response: dict) -> None:
    if not ENABLED:
        return
    blocks = response.get("content") or []
    text = " ".join(b.get("text", "") for b in blocks if b.get("type") == "text").strip()
    tools = [b.get("name") for b in blocks if b.get("type") == "tool_use"]
    usage = response.get("usage") or {}
    _write({
        "headset": headset,
        "reply": text,
        "tools": tools,
        "stop": response.get("stop_reason"),
        "tokens": {k: usage.get(k) for k in ("input_tokens", "cache_read_input_tokens",
                                              "cache_creation_input_tokens", "output_tokens")},
    })
