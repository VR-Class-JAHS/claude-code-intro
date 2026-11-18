# VR Physics Lab: notes for Claude Code

A Unity VR physics lab (Meta Quest 3/3S) where a Claude-powered assistant sets up experiments through tool use. Built as a learning project that will become a teachable workshop.

## Read first
- `PROGRESS.md`: what works, what's tested, how to start a session, next steps. Start with its "Next session" section.
- Project plan (Week 1 checklist, tool table, dev log): https://claude.ai/artifact/Gix4UhJHzP5EFNaNtT9B8z. It's a Claude Docs doc: read and edit it with the Claude Docs tools. Keep its checklist and dev log current as work lands.

## Layout
- `vr-lab-proxy/`: FastAPI proxy that holds the Anthropic API key (`main.py`) and announces itself on the Wi-Fi (`discovery.py`).
- `claude-code-intro/`: the Unity project (Unity 6000.3.28f1, URP, XR Interaction Toolkit 3.5, OpenXR for Android/Quest).
  - `Assets/ClaudeLab/Scripts/`: `ClaudeAgent` (tool loop), `ClaudeLabPanel` (in-headset UI) and helpers.
  - `Assets/ClaudeLab/Scripts/Tools/`: one `LabTool` subclass per tool. New tools also go in `ToolTypes` in `Editor/ClaudeLabSetup.cs`.
  - `Assets/ClaudeLab/Editor/`: the **Claude Lab** menu (add assistant, add panel, proxy settings).

## How we work
- One branch and pull request per piece of work; the user merges. Commit or push only when asked.
- Create branches with `--no-track` (e.g. `git switch -c <name> --no-track origin/main`) so they have no upstream and GitHub Desktop offers to publish them as new branches.
- The user tests every change in Play mode (XR Interaction Simulator) and on the Quest before merging. Say plainly what has and hasn't been tested.
- Check what a PR actually contains before calling work merged (one commit once landed after its PR was merged).
- Unity rewrites scene and settings files. Before committing, look at what changed and leave out unrelated churn: `ProjectSettings/ProjectSettings.asset` and `UnityConnectSettings.asset` changes that clear the Unity Cloud link, line-ending-only changes, the TextMesh Pro fallback font asset, an empty `Assets/Resources` folder.
- Editing `SampleScene.unity` on disk while Unity has it open: tell the user to choose Reload, and not to save before reloading.

## Never commit
- `vr-lab-proxy/.env` (API key and proxy token; the file must be named `.env` with the dot).
- `Assets/ClaudeLab/Settings/Resources/ClaudeProxySettings.asset` (holds the token; gitignored). Each computer has its own; code loads it by name, so scenes never link to it.
- Builds (`*.apk`), `*_BurstDebugInformation_DoNotShip/` and `*_BackUpThisFolder_ButDontShipItWithYourGame/` (all gitignored).
- Settings files a build rewrites without a real change: the URP configs (line endings), `SampleScene.scenetemplate`, `OpenXRPackageSettings.asset` (it holds two copies of some Android XR features, and builds swap which one the list points to; keep *Android XR Support* off for Quest), and *Preloaded Assets* in `ProjectSettings.asset` (XR settings that builds add and remove).
