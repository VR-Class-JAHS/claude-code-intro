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

## When the school network blocks Anthropic (Tailscale)

Only the computer running the proxy needs to reach `api.anthropic.com`. Headsets and student computers only talk to the proxy over the school network. If the school blocks Anthropic, send that one computer's internet traffic through a computer at home with Tailscale:

```
headsets / student computers ──school Wi-Fi──▶ proxy computer ──Tailscale──▶ home laptop ──▶ api.anthropic.com
```

Check with school IT first: routing around the web filter may go against the network's acceptable-use policy. Asking them to allow `api.anthropic.com` (HTTPS, port 443), like the Meta exception, would be simpler still.

### Home laptop (once)

1. Install Tailscale from tailscale.com/download and sign in.
2. Tailscale tray icon → **Exit nodes** → **Run exit node**.
3. In the admin console (login.tailscale.com/admin/machines), open the laptop's **⋯** menu:
   - **Edit route settings** → tick **Use as exit node**.
   - **Disable key expiry**, so the laptop doesn't drop off the tailnet after a few months.
4. Tailscale tray icon → **Preferences** → **Run unattended**, so it keeps working after a restart before anyone logs in.
5. Windows Settings → **Power**: never sleep when plugged in, and *Do nothing* when the lid is closed. Leave it plugged in and on Wi-Fi or Ethernet.

### School proxy computer (once, then each class)

1. Install Tailscale and sign in to the same account. On a managed computer this may need IT or admin rights. If it never connects on the school Wi-Fi, the filter blocks Tailscale too.
2. Tailscale tray icon → **Exit nodes** → choose the home laptop, and tick **Allow local network access**. Without that box, headsets and student computers can't reach the proxy.
3. Check that Anthropic is reachable. A 4xx code such as `401`, `404` or `405` means Anthropic answered; a hang, `000`, or a redirect code means the traffic is still being filtered:
   ```powershell
   curl.exe -s -o NUL -w "%{http_code}`n" https://api.anthropic.com/v1/messages
   ```
4. Start the proxy as usual and run the tests below, plus `/health` from another device on the school Wi-Fi.

While the exit node is on, all of this computer's internet traffic goes through home, not just Claude's. Turn it off (**Exit nodes** → **None**) after class. If the home laptop is off or offline, `/chat` returns 502 *could not reach Claude API*; a phone hotspot on the proxy computer is the fallback.

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
