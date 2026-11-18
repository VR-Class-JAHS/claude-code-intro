# VR Lab + Claude: where we are

Last updated: 2026-11-17

## Working now

- **vr-lab-proxy/**: FastAPI server that holds the Anthropic API key. Tested: `/health`, `/chat`, a wrong token returns 401, and a two-round `spawn_object` tool exchange.
- **Unity client** (`claude-code-intro/Assets/ClaudeLab/`): `ClaudeAgent` runs the tool loop through the proxy. Tools: `spawn_object` (ball, block, pendulum, ramp) and `clear_lab`. Tested in Play mode in `SampleScene`: pressing **T** spawns a ball and a block that fall. **R** clears the lab.
- `SampleScene` already contains the **Claude Lab Assistant** object.
- Player Settings → *Allow downloads over HTTP* = Always allowed (the proxy uses plain http).

## Written, not yet tested in Unity

- **Prompt panel** (`Scripts/ClaudeLabPanel.cs`, `Editor/ClaudeLabPanelSetup.cs`): menu **Claude Lab → Add Prompt Panel to Scene** builds a world-space canvas to the left of the spawn area. It has preset prompt buttons (edit the *Presets* list, then ⋮ → *Rebuild Buttons*), a reply box, a status line, and a *New conversation* button. Buttons are disabled while Claude is working. To test: run the menu item, press Play, click a button with the mouse or XR simulator.
- **New tools** (`Scripts/Tools/`):
  - `measure_object`: height, speed and mass of spawned objects, plus each ball or block's last drop (release height, fall time, impact speed). `FallRecorder`, added to every ball and block, records drops every physics step, including when a student grabs an object and lets go. That matters because Claude's calls arrive after the object has landed.
  - `lab_timer`: start, stop, reset, read. Shown on the panel's Timer line.
  - `set_gravity`: Earth, Moon, Mars, Jupiter, zero, or a number up to 30 m/s². Restored when Play mode ends.
  - To add them to the existing assistant in `SampleScene`, run **Claude Lab → Add Lab Assistant to Scene** again: it now adds any missing tools, then save the scene. Do this *before* adding the prompt panel so the panel's Timer line gets connected.
- **Proxy settings link**: `SampleScene` points to a `ClaudeProxySettings` asset that only existed on the old computer. **Claude Lab → Select Proxy Settings** now reconnects the new asset to the assistant automatically. Save the scene afterwards.

## Setup gotcha

- The keys file must be named `.env` (with the dot). A file named `env` is not read by the proxy and is not ignored by git.

## Not in git (recreate on a new computer)

| What | Where | How to recreate |
|---|---|---|
| API key + proxy token | `vr-lab-proxy/.env` | Copy `.env.example` to `.env` and fill it in. Carry the values over privately (password manager or a USB stick, not email or git). |
| Python environment | `vr-lab-proxy/.venv` | `python -m venv .venv` then `.venv\Scripts\pip install -r requirements.txt` |
| Unity token asset | `Assets/ClaudeLab/Settings/ClaudeProxySettings.asset` | After `.env` exists: in Unity, **Claude Lab → Select Proxy Settings** (creates it and copies the token from `.env`). |

## Start a session

1. In `vr-lab-proxy/`, run `.venv\Scripts\python -m uvicorn main:app --port 8000`
2. Open `claude-code-intro/` in Unity 6000.3.23f1 and open `Assets/Scenes/SampleScene`.
3. Press Play, click the Game view, press **T**. (If the XR simulator grabs the T key, use the *Claude Test Prompt* component's ⋮ menu → *Send Prompt*.)

## Ideas for next steps

- A way for students to talk to the assistant: a world-space panel with preset prompt buttons first, then voice input.
- Show Claude's replies on a panel in the headset instead of only in the Console.
- More tools: read an object's speed or height, start or stop a timer, change gravity.
- Try it on the headset: set `proxyUrl` to `http://<this computer's IP>:8000` and allow port 8000 through the firewall.
- Check the pendulum and ramp visually. Only the ball and block have been tested in Play mode.
