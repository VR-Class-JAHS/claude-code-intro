# VR Lab + Claude: where we are

Last updated: 2026-11-17

## Working now

- **vr-lab-proxy/**: FastAPI server that holds the Anthropic API key. Tested: `/health`, `/chat`, a wrong token returns 401, and a two-round `spawn_object` tool exchange.
- **Unity client** (`claude-code-intro/Assets/ClaudeLab/`): `ClaudeAgent` runs the tool loop through the proxy. Tools: `spawn_object` (ball, block, pendulum, ramp) and `clear_lab`. Tested in Play mode in `SampleScene`: pressing **T** spawns a ball and a block that fall. **R** clears the lab.
- `SampleScene` already contains the **Claude Lab Assistant** object.
- Player Settings → *Allow downloads over HTTP* = Always allowed (the proxy uses plain http).

## Added 2026-11-17 (panel tested in Play mode on the second laptop)

- **Prompt panel** (`Scripts/ClaudeLabPanel.cs`, `Editor/ClaudeLabPanelSetup.cs`): menu **Claude Lab → Add Prompt Panel to Scene** builds a world-space canvas to the left of the spawn area. It has preset prompt buttons (edit the *Presets* list, then ⋮ → *Rebuild Buttons*), a reply box, a status line, and a *New conversation* button. Buttons are disabled while Claude is working. To test: run the menu item, press Play, click a button with the mouse or XR simulator.
- **New tools** (`Scripts/Tools/`):
  - `measure_object`: height, speed and mass of spawned objects, plus each ball or block's last drop (release height, fall time, impact speed). `FallRecorder`, added to every ball and block, records drops every physics step, including when a student grabs an object and lets go. That matters because Claude's calls arrive after the object has landed.
  - `lab_timer`: start, stop, reset, read. Shown on the panel's Timer line.
  - `set_gravity`: Earth, Moon, Mars, Jupiter, zero, or a number up to 30 m/s². Restored when Play mode ends.
  - To add them to the existing assistant in `SampleScene`, run **Claude Lab → Add Lab Assistant to Scene** again: it now adds any missing tools, then save the scene. Do this *before* adding the prompt panel so the panel's Timer line gets connected.
- **Proxy settings link**: `SampleScene` points to a `ClaudeProxySettings` asset that only existed on the old computer. **Claude Lab → Select Proxy Settings** now reconnects the new asset to the assistant automatically. Save the scene afterwards.

## Headset setup (2026-11-17, Meta Quest 3/3S)

- **Auto-discovery** (`vr-lab-proxy/discovery.py`, `Scripts/ProxyDiscovery.cs`): the proxy broadcasts a signed "I'm here" on UDP 47800 every 2 s; the headset connects to whichever computer sent it, so one build works on any network where broadcasts get through. Falls back to `proxyUrl` in the settings asset. Tested: announcements received and verified on this laptop; a wrong token is rejected. **Not yet tested on the headset.** Run **Claude Lab → Add Lab Assistant to Scene** once to add the `ProxyDiscovery` component, then save the scene.
- Proxy must run with `--host 0.0.0.0` (see Start a session).
- Firewall: inbound TCP 8000 allowed from the local subnet (rule *VR Lab proxy (port 8000)*, added on this laptop). Needed on every computer that runs the proxy.
- Build: Android Build Support module, Build Profiles → Android → Switch Platform, Run Device = Quest, Project Validation → Fix All, Build and Run.

## Setup gotcha

- The keys file must be named `.env` (with the dot). A file named `env` is not read by the proxy and is not ignored by git.

## Not in git (recreate on a new computer)

| What | Where | How to recreate |
|---|---|---|
| API key + proxy token | `vr-lab-proxy/.env` | Copy `.env.example` to `.env` and fill it in. Carry the values over privately (password manager or a USB stick, not email or git). |
| Python environment | `vr-lab-proxy/.venv` | `python -m venv .venv` then `.venv\Scripts\pip install -r requirements.txt` |
| Unity token asset | `Assets/ClaudeLab/Settings/ClaudeProxySettings.asset` | After `.env` exists: in Unity, **Claude Lab → Select Proxy Settings** (creates it and copies the token from `.env`). |

## Start a session

1. In `vr-lab-proxy/`, run `.venv\Scripts\python -m uvicorn main:app --host 0.0.0.0 --port 8000`
2. Open `claude-code-intro/` in Unity 6000.3.28f1 (upgraded from 23f1 on 2026-11-17) and open `Assets/Scenes/SampleScene`.
3. Press Play, point a controller ray at a panel button and press **T** (the simulator trigger; **G** grabs, **Tab** switches controller). Keep the *Claude Test Prompt* component unticked: it also listens for T and would send a second request.

## Ideas for next steps

- Voice input, so students can ask their own questions instead of only the preset buttons.
- Show the panel near the student wherever they stand (follow or summon it).
- Check each new tool on its own: measure_object after a student drop, lab_timer, set_gravity on Moon and zero.
- Finish the first headset build and test (headset was out of battery).
- Check whether school Wi-Fi passes UDP broadcasts and device-to-device traffic; if not, look at a hotspot or a dedicated router for the cart.
- Check the pendulum and ramp visually. Only the ball and block have been tested in Play mode.
