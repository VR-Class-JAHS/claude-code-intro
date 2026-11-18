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

## Classroom network: Meta router + Tailscale exit node

Tested 2026-11-18 on the school computer with a Quest 3S. In class, the headsets and the proxy computer all join the classroom's Meta router (`NETGEAR_11ac`). That network reaches Meta but blocks `api.anthropic.com`, so the proxy computer sends its internet traffic through a small cloud server (`vr-lab-exit`) with Tailscale:

```
headsets ──Meta router──▶ proxy computer ──Tailscale──▶ vr-lab-exit (cloud) ──▶ api.anthropic.com
```

Only the proxy computer needs Tailscale. Headsets only talk to the proxy, and on the Meta router they find it through auto-discovery with no address typed in.

Why not the school Wi-Fi (`ncpsp`): it blocks Anthropic too, and a headset on it never connected to the proxy. The announcements didn't arrive, so it fell back to `proxyUrl` (`localhost`). Whether `ncpsp` lets devices reach each other directly wasn't tested.

Check with school IT: routing around the web filter may go against the network's acceptable-use policy. If they allow `api.anthropic.com` (HTTPS, port 443) on the Meta router, like the Meta exception, the exit node isn't needed.

### Cloud exit node (once)

A DigitalOcean Basic droplet, $4/month (512 MB). It's billed by the hour, and a powered-off droplet still bills: **Destroy** it to stop charges.

1. **Create → Droplets**: region New York (any datacenter), image Ubuntu 24.04 LTS, size Basic → Regular → $4/mo, authentication Password (long and random, kept in a password manager), hostname `vr-lab-exit`.
2. When it shows **Active**, click **Web Console** (next to **Actions**) and run, one at a time:
   ```bash
   curl -fsSL https://tailscale.com/install.sh | sh
   echo 'net.ipv4.ip_forward = 1' | sudo tee -a /etc/sysctl.d/99-tailscale.conf
   echo 'net.ipv6.conf.all.forwarding = 1' | sudo tee -a /etc/sysctl.d/99-tailscale.conf
   sudo sysctl -p /etc/sysctl.d/99-tailscale.conf
   sudo tailscale up
   sudo tailscale set --advertise-exit-node
   ```
   `tailscale up` prints a login link: open it and sign in. On 2026-11-18, running `set --advertise-exit-node` before signing in didn't stick, and running it again after signing in fixed it, hence this order.
3. At login.tailscale.com/admin/machines, open `vr-lab-exit`'s **⋯** menu: **Edit route settings** → tick **Use as exit node**, then **Disable key expiry**.

4. Lock it down so it's reachable only through Tailscale (done 2026-11-18):
   - In the droplet's Web Console: `sudo tailscale set --ssh`. Your tailnet's **Tailscale SSH** policy needs a rule letting you reach your own devices (the default: `autogroup:member` → `autogroup:self` as `autogroup:nonroot, root`).
   - From your computer, check `ssh root@vr-lab-exit` logs in without a password. The first time, answer `yes` to the new host key.
   - DigitalOcean **Networking → Firewalls → Create Firewall** (`vr-lab-exit-lockdown`): no inbound rules, default outbound rules, applied to `vr-lab-exit`.
   - Over `ssh root@vr-lab-exit`: `systemctl disable --now ssh.socket ssh.service`, which turns off the normal SSH server so nothing answers port 22 from the internet. Tailscale SSH keeps working.

   Afterwards, manage the droplet with `ssh root@vr-lab-exit`. DigitalOcean's Web Console no longer works. If Tailscale ever breaks on it, use **Access → Recovery Console** with the root password, and `systemctl enable --now ssh.socket` turns the normal SSH server back on.

   Testing tip: with the exit node on, connecting to the droplet's *public* IP goes through the tunnel and bypasses the DigitalOcean firewall, so it can't test the firewall. After the lockdown, the public IP refuses port 22 even that way.

A computer at home can be the exit node instead: install Tailscale, turn on **Run exit node** in its tray menu, approve it the same way, and keep it awake.

### Proxy computer (once)

1. Install Tailscale for Windows and sign in to the same account (needs admin rights).
2. Add the firewall rule from **Setup** above. Windows marks both school networks as Public; the rule covers that.

### Each class

1. Connect the proxy computer to `NETGEAR_11ac` (Wi-Fi, or Ethernet to a LAN port).
2. Tailscale tray icon → **Use exit node** → `vr-lab-exit`, and tick **Allow local network access**. Without that box, headsets can't reach the proxy.
3. Check that Anthropic is reachable. A 4xx code such as `401`, `404` or `405` means Anthropic answered; a hang, `000`, or a redirect code means it's still filtered:
   ```powershell
   curl.exe -s -o NUL -w "%{http_code}`n" https://api.anthropic.com/v1/messages
   ```
4. Start the proxy with `--host 0.0.0.0`. Headsets on `NETGEAR_11ac` find it automatically. To check from a headset, open `http://<proxy computer's address>:8000/health` in the Quest browser.
5. After class: **Use exit node** → **None**. While it's on, all of the computer's internet traffic goes through the cloud server.

If the exit node is unreachable, `/chat` returns 502 *could not reach Claude API*; a phone hotspot on the proxy computer is the fallback.

Notes:
- The school Wi-Fi gives out `100.x` addresses, the same range Tailscale uses. Checked on 2026-11-18: traffic to other devices on that network still goes over Wi-Fi with the exit node on.
- If the router hands the proxy computer a new address each day, only the `proxyUrl` fallback cares. A DHCP reservation in the router keeps it fixed.
- If headsets can't reach the proxy on the router, look for client (AP) isolation in its settings. It wasn't needed on 2026-11-18.

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
