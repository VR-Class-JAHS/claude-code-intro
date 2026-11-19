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

## Voice input

`POST /transcribe` (same `X-Proxy-Token` header as `/chat`) takes a 16-bit PCM WAV of up to 20 s and returns `{"text": ...}`. It runs Whisper on this computer with faster-whisper (`speech.py`), so students' recordings never leave it; only the text goes on to Claude. The Unity panel's **Hold to talk** button uses it.

- Needs Python 3.12 or 3.13: faster-whisper's PyAV dependency has no Python 3.15 build yet. Create the environment with `py -3.13 -m venv .venv`.
- The model (`base.en`, about 145 MB) downloads from Hugging Face the first time the proxy starts, in the background, and is cached in `%USERPROFILE%\.cache\huggingface`. On the classroom network it downloads through the exit node like everything else.
- `.env` options: `WHISPER_MODEL=tiny.en` (faster) or `small.en` (more accurate); `WHISPER=0` turns voice input off.
- **Vocabulary hint** (2026-11-19): Whisper is given a short hint of the lab's words and phrasing (`DEFAULT_PROMPT` in `speech.py`). On 48 spoken test clips it raised exact transcriptions from 38 to 46, fixed "spheres" heard as "spears" and "land" heard as "landed", and stopped silence or noise coming out as "you". `WHISPER_PROMPT=...` in `.env` replaces it; `WHISPER_PROMPT=` (empty) turns it off. Restart the proxy after changing it.
- On a laptop CPU, a few seconds of speech takes about half a second. Requests are handled one at a time, so many students talking at once will queue.

## Class use: caching, rate limits, question log

- **Prompt caching** is on for every `/chat` request: the tool list and system prompt (about 2,900 tokens, the same for every headset) and each conversation so far are cached. Cached tokens cost a tenth as much and don't count toward the API key's input-tokens-per-minute limit, which is what lets a class share one key. A burst of 12 simulated students on 2026-11-18: all answered, median wait 7.4 s, nothing rate-limited.
- **Rate limits**: the SDK retries 429 and overload errors with backoff, up to `MAX_RETRIES` times (default 4). If a request is still limited, `/chat` returns 429 with a message the panel shows ("Claude is busy… ask again").
- **Question log** (off by default): `LOG_QUESTIONS=1` in `.env` writes each question and answer round to `logs/YYYY-MM-DD.jsonl` (gitignored): time, the headset's address, the text, the tools used and token counts. Text only, no audio, no names. Check with school policy before keeping student questions, and delete the files when you're done with them.

## Classroom network: Meta router + NetBird exit node

Tested 2026-11-19 on the school computer with a Quest 3S: two spoken questions from the headset were transcribed and answered. In class, the headsets and the proxy computer all join the classroom's Meta router (`NETGEAR_11ac`). That network reaches Meta but blocks `api.anthropic.com`, so the proxy computer sends its internet traffic through a small cloud server (`vr-lab-exit`) with **NetBird**:

```
headsets ──Meta router──▶ proxy computer ──NetBird relay (HTTPS 443)──▶ vr-lab-exit (cloud) ──▶ api.anthropic.com
```

Only the proxy computer needs NetBird. Headsets only talk to the proxy, and on the Meta router they find it through auto-discovery with no address typed in.

Why NetBird and not Tailscale: Tailscale worked on the Meta router on 2026-11-18, but on 2026-11-19 it stopped. The tunnel connected, almost no traffic came back through it, and Tailscale's relays and address checks got no answer on that network. NetBird falls back to a relay over ordinary HTTPS (TCP 443) when direct connections are blocked, and that gets through. Tailscale stays installed on the droplet and the proxy computer only to manage the droplet (`ssh root@vr-lab-exit`) from the hotspot or from home.

Why not the school Wi-Fi (`ncpsp`): it blocks Anthropic too, and a headset on it never connected to the proxy. The announcements didn't arrive, so it fell back to `proxyUrl` (`localhost`). Whether `ncpsp` lets devices reach each other directly wasn't tested.

School IT knows about this setup: local IT suggested NetBird on 2026-11-19. They won't ask central IT for an `api.anthropic.com` exception (the Meta one already existed for programs that use social media).

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

   On 2026-11-19 an inbound rule for **UDP 41641** (Tailscale's port, all IPv4 and IPv6) was added to `vr-lab-exit-lockdown`, so Tailscale can connect directly from networks with strict address translation. SSH stays off; Tailscale only accepts your own devices.

5. Add NetBird (done 2026-11-19), on the hotspot or at home:
   - Sign up at app.netbird.io. **Setup Keys → Create Setup Key**: one-time use, expires in 1 day, *Ephemeral peers* off. The key only adds the server; it stays connected after the key expires.
   - `ssh root@vr-lab-exit`, then:
     ```bash
     curl -fsSL https://pkgs.netbird.io/install.sh | sh
     netbird up --setup-key <your key>
     ```
     The NetBird service starts on boot. IP forwarding is already on from step 2.
   - NetBird dashboard: **Peers → vr-lab-exit → Add Exit Node**, distribution group *All*, **Auto Apply** off (choose it per class). The default *All → All* access policy lets your devices use it.
   - **DNS → Nameservers**: add one (Cloudflare or Google) with match domain **ALL**. The school's DNS servers don't answer through the exit node.
   - Check with `netbird status` on the droplet: *Management* and *Signal* connected, *Networks* `0.0.0.0/0, ::/0`.

   NetBird needs no inbound ports on the droplet: when it can't connect directly, it uses its relay over HTTPS 443.

Droplet upkeep: ignore the *New release '26.04 LTS' available* message (24.04 is supported for years). When it says *System restart required*, run `reboot` over `ssh root@vr-lab-exit`, wait a minute, and reconnect. Tailscale and NetBird both start on boot.

### Proxy computer (once)

1. Install the **NetBird** Windows app (needs admin rights) and sign in to the same NetBird account.
2. Install **Tailscale** for Windows and sign in, only for `ssh root@vr-lab-exit`. Its exit node stays on **None**: two exit nodes at once would conflict.
3. Add the firewall rule from **Setup** above. Windows marks both school networks as Public; the rule covers that.

### Each class

1. NetBird tray icon → exit node → `vr-lab-exit`. At school, do this *before* opening Claude Code: it needs the exit node to reach Anthropic too. Tailscale's exit node stays on **None**.
2. Connect the proxy computer to `NETGEAR_11ac` (Wi-Fi, or Ethernet to a LAN port).
3. Check that Anthropic is reachable. A 4xx code such as `401`, `404` or `405` means Anthropic answered; a hang, `000`, or a redirect code means it's still filtered:
   ```powershell
   curl.exe -s -o NUL -w "%{http_code}`n" https://api.anthropic.com/v1/messages
   ```
   For details, `& 'C:\Program Files\NetBird\netbird.exe' status -d` should show `vr-lab-exit` as *Connected*. On the Meta router the connection type is *Relayed*; on the hotspot it's *P2P*.
4. Start the proxy with `--host 0.0.0.0`. Headsets on `NETGEAR_11ac` find it automatically. To check from a headset, open `http://<proxy computer's address>:8000/health` in the Quest browser.
5. After class: deselect the exit node in NetBird. While it's on, all of the computer's internet traffic goes through the cloud server.

If the exit node is unreachable, `/chat` returns 502 *could not reach Claude API*; a phone hotspot on the proxy computer is the fallback.

Notes:
- With NetBird's exit node on, traffic to the Meta router's own network (`10.79.110.0/24`) stays on Wi-Fi, so headsets still reach the proxy (checked 2026-11-19). NetBird needs no *Allow local network access* setting for this.
- The school Wi-Fi gives out `100.x` addresses, the same range Tailscale and NetBird use. Checked on 2026-11-18 with Tailscale: traffic to other devices on that network still goes over Wi-Fi with the exit node on.
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
