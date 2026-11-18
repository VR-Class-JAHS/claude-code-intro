# VR Lab Claude Proxy

A thin server that holds your Anthropic API key so it never ships inside the Unity build.

## Setup

```bash
pip install -r requirements.txt
cp .env.example .env      # then fill in your API key and a random PROXY_TOKEN
uvicorn main:app --host 0.0.0.0 --port 8000 --reload
```

`--host 0.0.0.0` lets a standalone headset on the same Wi-Fi reach it at `http://<your-computer's-IP>:8000`. In the editor, `http://localhost:8000` works.

Windows blocks incoming connections on Public networks, so allow port 8000 once (admin PowerShell):

```powershell
New-NetFirewallRule -DisplayName "VR Lab proxy (port 8000)" -Direction Inbound -Protocol TCP -LocalPort 8000 -RemoteAddress LocalSubnet -Action Allow
```

## Auto-discovery

While running, the proxy broadcasts a small signed announcement on UDP port 47800 every 2 seconds (`discovery.py`). The Unity `ProxyDiscovery` component listens for it and connects to whichever computer sent it, so headset builds don't need the IP address typed in, and keep working when the address changes.

- The announcement is signed with `PROXY_TOKEN` (HMAC-SHA256) and never contains the token. Headsets ignore announcements with a wrong signature, a mismatched sender address, or an old timestamp.
- If the network drops broadcasts (some school Wi-Fi does), headsets fall back to `proxyUrl` in the Unity settings asset.
- `PROXY_PORT` in `.env` must match uvicorn's `--port` if you change it from 8000. `ANNOUNCE=0` turns announcements off.

## Test it (session 1 "done")

```bash
curl http://localhost:8000/health

curl -X POST http://localhost:8000/chat \
  -H "Content-Type: application/json" \
  -H "X-Proxy-Token: <your PROXY_TOKEN>" \
  -d '{"messages":[{"role":"user","content":"Say hello in five words."}]}'
```

## What Unity sends

`POST /chat` with header `X-Proxy-Token` and body:

```json
{
  "system": "You are a lab assistant in a VR physics lab...",
  "tools": [
    {
      "name": "spawn_object",
      "description": "Create a physics object in the lab.",
      "input_schema": {
        "type": "object",
        "properties": {
          "type": {"type": "string", "enum": ["ball", "block", "pendulum", "ramp"]},
          "height_m": {"type": "number"}
        },
        "required": ["type"]
      }
    }
  ],
  "messages": [{"role": "user", "content": "Drop a ball from 2 meters."}]
}
```

The response is Claude's message unchanged. If `stop_reason` is `"tool_use"`, Unity runs each `tool_use` block, appends Claude's message plus a user message of `tool_result` blocks to `messages`, and calls `/chat` again. Stop when `stop_reason` is `"end_turn"` or after 8 rounds.

## Notes

- `CLAUDE_MODEL` in `.env` lets you switch models; a smaller, faster model is worth trying once latency matters in VR.
- Never commit `.env`. Add it to `.gitignore`.
- API reference: https://docs.claude.com
