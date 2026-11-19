# Unity MCP server for the VR lab

Lets Claude Code inspect and drive the Unity Editor while you work: list GameObjects, read and change component
settings, enter and exit Play mode, read the Console, run **Claude Lab** menu items, and, in Play mode, ask the lab
assistant a question, press Release and read what happened.

```
Claude Code ──MCP (stdio)──▶ unity-mcp/server.py ──TCP 127.0.0.1:8765 + token──▶ Claude Lab Bridge (in the Unity Editor)
```

- **Bridge** (`unity-project/Assets/ClaudeLab/Editor/ClaudeLabBridge.cs`): starts with the Unity Editor and listens on
  `127.0.0.1` only. Each request needs a random token that Unity writes to `Library/ClaudeLabBridge/token.txt`
  (`Library` isn't in git), so only programs on this computer that can read the project folder can use it.
  Unity → **Claude Lab → Bridge Status** shows whether it's listening.
- **MCP server** (`server.py`): Claude Code starts it from `.mcp.json` at the repo root. It retries for up to 45 s while
  Unity reloads scripts (for example when entering Play mode).

## Setup (once per computer)

```powershell
cd unity-mcp
py -3.13 -m venv .venv          # Python 3.10 or newer
.venv\Scripts\pip install -r requirements.txt
```

Then open Unity on the project, start a **new** Claude Code session in the repo folder, and approve the
`unity-vr-lab` server when Claude Code asks. `/mcp` in Claude Code shows whether it's connected.

The session can start in the repo folder or in the Unity project folder inside it (`unity-project`): `.mcp.json` starts the server through `cmd`, which looks for `unity-mcp\server.py` in the
session's folder and then one folder up. This makes the setup Windows-only, like the `.venv\Scripts` path already is.

## Tools

| Tool | What it does |
|---|---|
| `unity_status` | Unity version, open scene and whether it has unsaved changes, Play mode, compiling, platform |
| `list_gameobjects` | Hierarchy paths with component types (`root`, `depth`) |
| `get_component` | A component's serialized properties, e.g. `Claude Lab Assistant` / `SpawnObjectTool` |
| `set_property` | Change one property (numbers, true/false, text, enum names, colors, vectors). Undoable; Edit-mode changes aren't saved, Play-mode changes are lost on exit |
| `play_mode` | `enter`, `exit` or `status`. Won't enter with unsaved scene changes |
| `console_logs` | Recent Console messages; `level` `error` or `warning` to filter |
| `run_menu_item` | Items under `Claude Lab/` only |
| `lab_ask` | Play mode: ask the assistant a question through the panel (spends API credit, like a student question) |
| `lab_release` | Play mode: press Release |
| `lab_status` | Play mode: busy or not, the status line, the reply, and every object in the lab |

Tested 2026-11-18: enter Play mode → `lab_ask` "Drop a ball from 2 meters." → `lab_status` showed the reply and the
ball waiting at 2 m → `lab_release` → the ball fell and came to rest → exit Play mode, no Console errors.

After some trips through Play mode (with these tools or the Play button), SampleScene shows unsaved changes. Undo History
(Window → General → Undo History) shows *Driving RectTransform*: Unity's UI layout re-sets the panel's sizes, and saving
writes the same values back. Choose **Don't Save** (reopen the scene) before the next `play_mode` `enter`.

Not included: running Unity Test Framework tests, saving scenes, building, and editor menus outside `Claude Lab/`.
