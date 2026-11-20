#!/bin/sh
# Restarts NetBird on the exit-node droplet (vr-lab-exit) when it has lost its connection to NetBird's
# management service for 3 checks in a row. Run every minute by netbird-watchdog.timer.
#
# Why: on 2026-11-20 the droplet's NetBird dropped its management connection ("error reading from
# server: EOF") and never reconnected by itself, so every client lost the exit node until
# `systemctl restart netbird` was run by hand.
#
# Test without restarting anything:
#   STATUS_CMD='echo Management: Disconnected' DRY_RUN=1 STATE=/tmp/nbw.count sh netbird-watchdog.sh  (x3)

STATUS_CMD=${STATUS_CMD:-netbird status}
STATE=${STATE:-/run/netbird-watchdog.count}
LIMIT=${LIMIT:-3}

if $STATUS_CMD 2>/dev/null | grep -q '^Management: Connected'; then
  echo 0 > "$STATE"
  exit 0
fi

n=$(( $(cat "$STATE" 2>/dev/null || echo 0) + 1 ))
echo "$n" > "$STATE"
if [ "$n" -lt "$LIMIT" ]; then
  logger -t netbird-watchdog "NetBird management disconnected ($n of $LIMIT checks)"
  exit 0
fi

echo 0 > "$STATE"
if [ -n "$DRY_RUN" ]; then
  logger -t netbird-watchdog "DRY RUN: would restart netbird after $n disconnected checks"
  echo "would restart netbird"
else
  logger -t netbird-watchdog "NetBird management disconnected for $n checks; restarting netbird"
  systemctl restart netbird
fi
