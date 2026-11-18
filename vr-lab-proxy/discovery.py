"""
Lets headsets find this proxy on the Wi-Fi without typing in the computer's IP address.

Every few seconds a small UDP broadcast goes out on each network this computer is on:

    {"service": "vr-lab-proxy", "ip": "<this computer's address on that network>",
     "port": 8000, "ts": <unix seconds>, "sig": "<hex HMAC-SHA256>"}

sig = HMAC-SHA256(key=PROXY_TOKEN, message="vr-lab-proxy|<ip>|<port>|<ts>").
The token itself is never sent. A headset (which knows the token) only accepts an
announcement whose signature matches, whose ip matches the packet's sender, and whose
ts is recent, so another device on the network can't pretend to be the proxy.

Outgoing broadcasts need no firewall change on this computer. Some school networks
drop broadcasts; the Unity client then falls back to the proxyUrl in its settings.
"""

import hashlib
import hmac
import json
import socket
import threading
import time

DISCOVERY_PORT = 47800
INTERVAL_S = 2.0


def sign(token: str, ip: str, port: int, ts: int) -> str:
    msg = f"vr-lab-proxy|{ip}|{port}|{ts}".encode()
    return hmac.new(token.encode(), msg, hashlib.sha256).hexdigest()


def local_ipv4_addresses() -> list[str]:
    """This computer's IPv4 addresses, skipping loopback and link-local."""
    addrs = set()
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            addrs.add(info[4][0])
    except socket.gaierror:
        pass
    # The address used for the default route, in case the hostname lookup misses it.
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            s.connect(("192.0.2.1", 9))  # no packet is sent for UDP connect
            addrs.add(s.getsockname()[0])
    except OSError:
        pass
    return sorted(a for a in addrs if not a.startswith(("127.", "169.254.")))


def _announce_forever(token: str, port: int, stop: threading.Event) -> None:
    while not stop.is_set():
        ts = int(time.time())
        for ip in local_ipv4_addresses():
            packet = json.dumps({
                "service": "vr-lab-proxy",
                "ip": ip,
                "port": port,
                "ts": ts,
                "sig": sign(token, ip, port, ts),
            }).encode()
            try:
                # Binding to the address makes Windows send the broadcast out of that network's adapter.
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
                    s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
                    s.bind((ip, 0))
                    s.sendto(packet, ("255.255.255.255", DISCOVERY_PORT))
            except OSError:
                pass  # adapter went away or doesn't allow broadcast; try again next round
        stop.wait(INTERVAL_S)


def start_announcer(token: str, port: int) -> threading.Event:
    """Start broadcasting in a background thread. Set the returned event to stop."""
    stop = threading.Event()
    threading.Thread(target=_announce_forever, args=(token, port, stop), daemon=True,
                     name="vr-lab-proxy-announcer").start()
    return stop
