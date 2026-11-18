"""
MCP server that lets Claude Code inspect and drive the Unity Editor running the VR lab.

Claude Code starts this over stdio (see .mcp.json at the repo root). Each tool forwards to the
Claude Lab Bridge inside the Unity Editor (Assets/ClaudeLab/Editor/ClaudeLabBridge.cs): TCP on 127.0.0.1:8765,
one JSON line each way, with a token the Editor writes to Library/ClaudeLabBridge/token.txt.

Unity must be open on the project. The bridge restarts whenever Unity reloads scripts (including entering
Play mode), so calls retry for a while instead of failing straight away.
"""

import json
import os
import socket
import time
from pathlib import Path

from mcp.server.mcpserver import MCPServer

PROJECT = Path(os.environ.get("UNITY_PROJECT", Path(__file__).resolve().parent.parent / "claude-code-intro"))
TOKEN_FILE = PROJECT / "Library" / "ClaudeLabBridge" / "token.txt"
PORT = int(os.environ.get("CLAUDE_LAB_BRIDGE_PORT", "8765"))
WAIT_FOR_UNITY_S = 45  # long enough for a script reload or entering Play mode

server = MCPServer(
    "unity-vr-lab",
    instructions=(
        "Inspect and drive the Unity Editor running the VR physics lab (Unity 6, XR Interaction Toolkit). "
        "GameObjects are addressed by hierarchy path, e.g. 'Claude Lab Assistant' or 'Claude Prompt Panel/Reply Box'. "
        "Start with unity_status and list_gameobjects. Property changes in Play mode are lost when it ends; changes in "
        "Edit mode mark the scene dirty but aren't saved: tell the user to save in Unity if they should be kept. "
        "lab_ask, lab_release and lab_status only work in Play mode; lab_ask spends API credit like a student question."
    ),
)


def _bridge(command: str, args: dict | None = None) -> str:
    """Send one command to the Unity bridge; retry while Unity is reloading. Returns the result as JSON text."""
    deadline = time.time() + WAIT_FOR_UNITY_S
    last_error = ""
    while True:
        try:
            token = TOKEN_FILE.read_text(encoding="utf-8").strip()
            with socket.create_connection(("127.0.0.1", PORT), timeout=5) as s:
                s.settimeout(30)
                s.sendall((json.dumps({"token": token, "command": command, "args": args or {}}) + "\n").encode())
                data = b""
                while not data.endswith(b"\n"):
                    chunk = s.recv(65536)
                    if not chunk:
                        break
                    data += chunk
            reply = json.loads(data.decode("utf-8"))
            if reply.get("ok"):
                return json.dumps(reply.get("result"), indent=1, ensure_ascii=False)
            error = reply.get("error", "unknown error")
            if error == "Bad token." and time.time() < deadline:  # Unity restarted and wrote a new token
                last_error = error
                time.sleep(1)
                continue
            raise RuntimeError(error)
        except (ConnectionRefusedError, ConnectionResetError, socket.timeout, FileNotFoundError, json.JSONDecodeError) as e:
            last_error = str(e)
            if time.time() >= deadline:
                raise RuntimeError(
                    f"Couldn't reach Unity on 127.0.0.1:{PORT} ({last_error}). Is the Unity Editor open on "
                    f"{PROJECT.name}? Unity → Claude Lab → Bridge Status shows whether the bridge is listening.")
            time.sleep(1)


@server.tool()
def unity_status() -> str:
    """Unity version, open scene (and whether it has unsaved changes), Play mode, compiling, build platform."""
    return _bridge("status")


@server.tool()
def list_gameobjects(root: str = "", depth: int = 2, components: bool = True) -> str:
    """List GameObjects as hierarchy paths with their component types. root: a path to start from (empty = every
    root object); depth: how many levels of children to include (0-6)."""
    return _bridge("list_gameobjects", {"root": root, "depth": depth, "components": components})


@server.tool()
def get_component(path: str, component: str) -> str:
    """Read a component's serialized properties (top level), e.g. path 'Claude Lab Assistant',
    component 'SpawnObjectTool'. Property names here are what set_property takes."""
    return _bridge("get_component", {"path": path, "component": component})


@server.tool()
def set_property(path: str, component: str, property: str, value: str) -> str:
    """Set one serialized property: numbers, true/false, text, enum names, colors ('#FF8800') and vectors
    ('1, 2, 3'). Undoable. In Edit mode it marks the scene dirty but doesn't save; in Play mode it's lost on exit."""
    try:
        parsed = json.loads(value)  # numbers and booleans arrive as text over MCP
    except json.JSONDecodeError:
        parsed = value
    return _bridge("set_property", {"path": path, "component": component, "property": property, "value": parsed})


@server.tool()
def play_mode(action: str = "status") -> str:
    """action: 'enter', 'exit' or 'status'. Entering refuses if the scene has unsaved changes. Entering reloads
    scripts, so the next call may take a few seconds."""
    return _bridge("play_mode", {"action": action})


@server.tool()
def console_logs(since: int = 0, max: int = 50, level: str = "all") -> str:
    """Recent Unity Console messages (since the last script reload). level: 'all', 'warning' (warnings and errors)
    or 'error'. Pass the returned latest_seq as since next time to get only new messages."""
    return _bridge("console_logs", {"since": since, "max": max, "level": level})


@server.tool()
def run_menu_item(item: str) -> str:
    """Run a menu item under 'Claude Lab/', e.g. 'Claude Lab/Add Lab Assistant to Scene' or
    'Claude Lab/Reset Prompt Panel Position'. Other menus are not allowed."""
    return _bridge("run_menu_item", {"item": item})


@server.tool()
def lab_ask(text: str) -> str:
    """Play mode only: ask the lab assistant a question, as a student would through the panel. Uses the proxy and
    spends API credit. Then poll lab_status until busy is false."""
    return _bridge("lab_ask", {"text": text})


@server.tool()
def lab_release() -> str:
    """Play mode only: press the panel's Release button (drops every waiting ball and block). Returns lab_status."""
    return _bridge("lab_release")


@server.tool()
def lab_status() -> str:
    """Play mode only: whether the assistant is busy, the panel's status line and reply, and every object in the
    lab (from get_scene_state)."""
    return _bridge("lab_status")


if __name__ == "__main__":
    server.run()
