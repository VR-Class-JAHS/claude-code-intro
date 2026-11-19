# VR Lab + Claude: where we are

Last updated: 2026-11-18

## Next session (start here)

Week 1 of the project plan is done, and the classroom network works on the school computer (see *Classroom network* below). Next:

1. **Proxy settings link fixed** (2026-11-18): the settings asset now lives in `Assets/ClaudeLab/Settings/Resources/` and is loaded by name, so the scene no longer links to it. On the laptop, Unity moved the old asset there by itself, and Play mode works (checked 2026-11-18).
2. **Build changes reviewed** (2026-11-18): every leftover change from the school build was noise and was discarded, including the old scene settings link. CLAUDE.md now lists the files builds rewrite. The build backup folder is gitignored (commit 97bcbf2, which went to `main` directly without a PR).
3. **`vr-lab-exit` locked down** (2026-11-18): Tailscale SSH on, a DigitalOcean firewall with no inbound rules, and OpenSSH turned off. Manage it with `ssh root@vr-lab-exit`; the DigitalOcean Web Console no longer works (Recovery Console does). Steps in `vr-lab-proxy/README.md`.
4. **Ask IT** whether `api.anthropic.com` can be allowed on the Meta router, like Meta. That would remove the need for the exit node.
5. **Measurement graphs** merged (PR #11), tested in Play mode and on the Quest.
6. **Voice input**, the scrolling reply box and the Exit lab button merged (PR #12).
7. **Classroom readiness** merged (PR #13). Next: the student trial (the VR class has 19 students, 1st period).
8. **Unity MCP server** on branch `unity-mcp` (2026-11-18), tested end to end; see `unity-mcp/README.md`. To use it: set up `unity-mcp/.venv` (once per computer), open Unity, start a new Claude Code session in the repo folder and approve the `unity-vr-lab` server. **On the school computer** the proxy's `.venv` must be rebuilt on Python 3.12/3.13 and `pip install -r requirements.txt` run again (see *Voice input* below). Details under *Measurement graphs* below.

Answered 2026-11-18: a class fits the API key's rate limits with prompt caching (see *Classroom readiness*). Open question: whether the school Wi-Fi (`ncpsp`) lets devices reach each other directly (not needed now that class uses the Meta router).

## Working now

- **vr-lab-proxy/**: FastAPI server that holds the Anthropic API key. Tested: `/health`, `/chat`, a wrong token returns 401, and a two-round `spawn_object` tool exchange.
- **Unity client** (`unity-project/Assets/ClaudeLab/`): `ClaudeAgent` runs the tool loop through the proxy. Tools: `spawn_object` (ball, block, pendulum, ramp) and `clear_lab`. Tested in Play mode in `SampleScene`: pressing **T** spawns a ball and a block that fall. **R** clears the lab. Pendulum and ramp also checked in Play mode (2026-11-17).
- `SampleScene` already contains the **Claude Lab Assistant** object.
- Player Settings → *Allow downloads over HTTP* = Always allowed (the proxy uses plain http).

## Added 2026-11-17 (panel tested in Play mode on the second laptop)

- **Prompt panel** (`Scripts/ClaudeLabPanel.cs`, `Editor/ClaudeLabPanelSetup.cs`): menu **Claude Lab → Add Prompt Panel to Scene** builds a world-space canvas to the left of the spawn area. It has preset prompt buttons (edit the *Presets* list, then ⋮ → *Rebuild Buttons*), a reply box, a status line, and a *New conversation* button. Buttons are disabled while Claude is working. To test: run the menu item, press Play, click a button with the mouse or XR simulator.
- **New tools** (`Scripts/Tools/`):
  - `measure_object`: height, speed and mass of spawned objects, plus each ball or block's last drop (release height, fall time, impact speed). `FallRecorder`, added to every ball and block, records drops every physics step, including when a student grabs an object and lets go. That matters because Claude's calls arrive after the object has landed.
  - `lab_timer`: start, stop, reset, read. Shown on the panel's Timer line.
  - `set_gravity`: Earth, Moon, Mars, Jupiter, zero, or a number up to 30 m/s². Restored when Play mode ends.
  - To add them to the existing assistant in `SampleScene`, run **Claude Lab → Add Lab Assistant to Scene** again: it now adds any missing tools, then save the scene. Do this *before* adding the prompt panel so the panel's Timer line gets connected.
- **Proxy settings link**: `SampleScene` pointed to a `ClaudeProxySettings` asset that only existed on the old computer. Replaced on 2026-11-18: the asset is now loaded from `Resources` by name and the scene doesn't link to it at all.

## Headset setup (2026-11-17, Meta Quest 3/3S)

- **Auto-discovery** (`vr-lab-proxy/discovery.py`, `Scripts/ProxyDiscovery.cs`): the proxy broadcasts a signed "I'm here" on UDP 47800 every 2 s; the headset connects to whichever computer sent it, so one build works on any network where broadcasts get through. Falls back to `proxyUrl` in the settings asset. Tested: announcements received and verified on this laptop; a wrong token is rejected. On the Quest 3S the app reached the proxy on home Wi-Fi, but it isn't confirmed whether through discovery or the `proxyUrl` fallback (both pointed at the same laptop). Run **Claude Lab → Add Lab Assistant to Scene** once to add the `ProxyDiscovery` component, then save the scene.
- Proxy must run with `--host 0.0.0.0` (see Start a session).
- Firewall: inbound TCP 8000 allowed from the local subnet (rule *VR Lab proxy (port 8000)*, added on this laptop). Needed on every computer that runs the proxy.
- Build: Android Build Support module, Build Profiles → Android → Switch Platform, Run Device = Quest, Project Validation → Fix All, Build and Run.

- **Release button** (tested in Play mode and on the Quest 3S, 2026-11-17): balls and blocks now spawn frozen in mid-air and drop together when a student presses the green **Release** button on the panel (no Claude round trip), or when Claude calls the new `release_objects` tool. Turn off with *Hold Until Release* on the spawn tool. Fall timing starts at release. Checked through the API: Claude sets objects up and tells the student to press Release.
- **Movable panel** (tested 2026-11-17): a *Grab here to move* bar along the bottom of the panel (`Scripts/PanelMover.cs`, added at Play time when the panel's *Movable* box is ticked). Grab it with a controller ray, hand, or the simulator's **G** (grip); the panel stays upright and turns to face you. Only the bar has a collider, so the buttons still click normally.
- **Panel starting spot** (tested 2026-11-17): new panels go about 45° to the left, 1.1 m away. **Claude Lab → Reset Prompt Panel Position** moves an existing panel there (then save the scene). The spawn row now fills from the middle outward, right side first, so the first objects land straight ahead.
- **Purple objects in builds fixed**: spawned shapes use `Assets/ClaudeLab/Materials/LabObject.mat` (URP Lit), assigned to the spawn tool's *Object Material* by **Claude Lab → Add Lab Assistant to Scene**. Tested on the Quest 3S.

- **Session 4 tools** (tested in Play mode and on the Quest 3S, 2026-11-17): `get_scene_state` lists every object (type, mass, height, position, speed, waiting/falling/resting, friction, bounciness) plus gravity and the timer. `set_property` changes an existing object's mass, height, velocity (with direction; a waiting object launches on Release), friction or bounciness. Simulated through the proxy: "make the ball twice as heavy and drop it from higher" chains get_scene_state -> mass 2 -> height 3, then asks the student to press Release. Run **Claude Lab -> Add Lab Assistant to Scene** to add them.

- **Typed input** (tested in Play mode 2026-11-17): a text box and Send button under the reply (`Scripts/TypedPromptInput.cs`, added at Play time). Enter also sends. Shown in the Editor and desktop builds, hidden on the Quest (*Typed Input Desktop Only* on the panel). While the box has focus the Input System keyboard is paused so typed letters don't drive the XR simulator. Covers Week 1 session 2's text box.

## Classroom network (tested 2026-11-18 on the school computer and a Quest 3S)

- The school network blocks `api.anthropic.com`, including on the classroom's Meta router (`NETGEAR_11ac`), which otherwise reaches Meta.
- Fix: the proxy computer uses **Tailscale** with a $4/month DigitalOcean droplet, `vr-lab-exit`, as its exit node (with *Allow local network access* on). Only the proxy computer needs it; headsets just talk to the proxy.
- Class runs on the Meta router: proxy computer and headsets both on `NETGEAR_11ac`. Auto-discovery worked there with no rebuild: the headset found the proxy and the panel answered.
- On the school Wi-Fi (`ncpsp`) the headset never connected: no announcements arrived, so it fell back to `proxyUrl` = `localhost`.
- Firewall rule *VR Lab proxy (port 8000)* added on the school computer.
- Setup and the per-class checklist: `vr-lab-proxy/README.md` → *Classroom network*.

- **Measurement graphs** (tested in Play mode and on the Quest 3S, 2026-11-18): `start_measurement` records height, speed, vertical velocity or energy (kinetic, potential from the floor, total) over 1–20 s, up to 4 lines, 25 readings a second. If the objects wait for Release, recording starts at Release (t = 0). `read_measurement` gives Claude each line's start, end, lowest and highest values with times, plus 12 evenly spaced readings. The panel shows a live graph under the reply (`MeasurementGraph`, `LineGraph`; *Show Graph* on the panel). New presets *Energy graph* and *Explain the graph*. The panel also turns Markdown in replies (`**bold**`, bullets, headings) into rich text. Simulated through the proxy: "drop a ball from 3 meters and graph its energy" chains spawn_object -> start_measurement(energy), then asks the student to press Release. Run **Claude Lab → Add Lab Assistant to Scene** to add the tools.

- **Voice input** (tested in Play mode and on the Quest 3S, 2026-11-18): a purple **Hold to talk** button under the reply (`Scripts/VoiceInput.cs`, *Show Voice Input* on the panel). Hold it (trigger, mouse or pinch) and speak; on release the recording (16 kHz WAV, up to 12 s) goes to the proxy's `/transcribe`, which turns it into text **on the proxy computer** with Whisper (`vr-lab-proxy/speech.py`, model `base.en`, CPU). The text then goes to Claude like a typed question. Recordings never leave that computer. Tested: a 3.5 s Windows text-to-speech recording transcribed correctly in about 0.5 s; a wrong token gets 401. On the Quest the app asks for microphone permission once.
- **Scrolling reply** (tested in Play mode and on the Quest 3S, 2026-11-18): long replies stay in the reply box at a fixed size and scroll (`Scripts/ScrollingReply.cs`); a scrollbar appears only when needed, and each new reply starts at the top. Drag the text or the scrollbar, or use the mouse wheel.
- **Exit lab button** (tested in Play mode and on the Quest 3S, 2026-11-18): beside *New conversation* (`Scripts/ExitButton.cs`, *Show Exit Button* on the panel). Press twice within 3 s to quit: back to the Quest home, or out of Play mode in the Editor.
  - Needs **Python 3.12 or 3.13** for the proxy: faster-whisper's PyAV dependency has no Python 3.15 build yet. Rebuilt the laptop's `.venv` on 3.13 (`py -3.13 -m venv --clear .venv`, then `pip install -r requirements.txt`).
  - The Whisper model (about 145 MB) downloads from Hugging Face the first time the proxy starts and is cached in `%USERPROFILE%\.cache\huggingface`. `WHISPER_MODEL` in `.env` picks another size; `WHISPER=0` turns voice off.

- **Classroom readiness** (2026-11-18, proxy changes tested with simulated students and in Play mode):
  - Account limits (read from the API's response headers): 1,000 requests, 500,000 input tokens and 80,000 output tokens per minute.
  - A lab request is about 2,900 input tokens before the conversation starts (mostly the 10 tool descriptions), and a question takes 2–3 rounds. Without caching, a class asking a couple of questions a minute would pass the input limit.
  - The proxy now uses **prompt caching**: the shared tools and system prompt, and each conversation so far, are read from cache (a tenth of the price, and not counted toward the input limit). It also retries rate limits up to 4 times (`MAX_RETRIES` in `.env`), and the panel shows "Claude is busy… ask again" if one still gets through.
  - Burst test, 12 simulated students asking at the same moment: all 12 answered, none rate-limited; median wait 7.4 s, slowest 15.9 s; 2.6 rounds per question; 86 uncached input tokens in total, everything else from cache. Cost about $0.004 per question on Claude Sonnet 5.5, so roughly $1.50 for 19 students asking 20 questions each.
  - **Question log** (off by default; `LOG_QUESTIONS=1` in `.env`): one line per question and per answer round in `vr-lab-proxy/logs/YYYY-MM-DD.jsonl` (gitignored), with the headset's address, the question or reply text, the tools used and token counts. Text only: no audio, no names. Use it after a class to see what students asked and where Claude got it wrong.

- **Unity MCP server** (2026-11-18, tested through an MCP client against the running Editor): `unity-mcp/server.py` (MCP Python SDK 2.x, Python 3.13) + `Assets/ClaudeLab/Editor/ClaudeLabBridge.cs` (TCP on 127.0.0.1:8765, per-Editor token in `Library/ClaudeLabBridge/token.txt`). Tools: unity_status, list_gameobjects, get_component, set_property, play_mode, console_logs, run_menu_item (Claude Lab menu only), lab_ask, lab_release, lab_status. Registered for Claude Code in `.mcp.json` at the repo root. Tested: entered Play mode, asked "Drop a ball from 2 meters", read the reply and the waiting ball, pressed Release, saw it land, exited Play mode, no Console errors.

## Setup gotcha

- The keys file must be named `.env` (with the dot). A file named `env` is not read by the proxy and is not ignored by git.

## Not in git (recreate on a new computer)

| What | Where | How to recreate |
|---|---|---|
| API key + proxy token | `vr-lab-proxy/.env` | Copy `.env.example` to `.env` and fill it in. Carry the values over privately (password manager or a USB stick, not email or git). |
| Python environment | `vr-lab-proxy/.venv` | Python 3.12 or 3.13 (not 3.15, for voice input): `py -3.13 -m venv .venv` then `.venv\Scripts\pip install -r requirements.txt` |
| MCP server environment | `unity-mcp/.venv` | `py -3.13 -m venv .venv` then `.venv\Scripts\pip install -r requirements.txt` (in `unity-mcp/`) |
| Unity token asset | `Assets/ClaudeLab/Settings/Resources/ClaudeProxySettings.asset` | After `.env` exists: in Unity, **Claude Lab → Select Proxy Settings** (creates it and copies the token from `.env`). No need to save the scene. |

## Start a session

1. In `vr-lab-proxy/`, run `.venv\Scripts\python -m uvicorn main:app --host 0.0.0.0 --port 8000`. At school, first follow *Each class* in `vr-lab-proxy/README.md` (Meta router + exit node).
2. Open `unity-project/` in Unity 6000.3.28f1 (upgraded from 23f1 on 2026-11-17) and open `Assets/Scenes/SampleScene`. The folder was called `claude-code-intro/` until 2026-11-18: on a computer that had it open before, close Unity before pulling, then in Unity Hub remove the old entry and **Add** `unity-project/`.
3. Press Play, point a controller ray at a panel button and press **T** (the simulator trigger; **G** grabs, **Tab** switches controller). Keep the *Claude Test Prompt* component unticked: it also listens for T and would send a second request.

## Ideas for next steps

- Voice input, so students can ask their own questions instead of only the preset buttons.
- Show the panel near the student wherever they stand (follow or summon it).
- Check each new tool on its own: measure_object after a student drop, lab_timer, set_gravity on Moon and zero.
- ~~Check whether school Wi-Fi passes UDP broadcasts and device-to-device traffic~~ Done 2026-11-18: class uses the Meta router instead (see *Classroom network*).
