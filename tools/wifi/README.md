# Switch the class laptops' Wi-Fi

`switch-wifi.cmd` moves a Windows laptop onto one Wi-Fi network: it saves the network and connects to it. The network and its password come from a file exported from a laptop that's already on it, so the password never goes in the script or in git.

## Once: export the network

On any laptop already connected to the network (for example `NETGEAR_11ac`), open Command Prompt and run, with your USB stick's drive letter:

```
netsh wlan export profile name="NETGEAR_11ac" key=clear folder=E:\
```

That writes a file like `E:\Wi-Fi-NETGEAR_11ac.xml`. **It holds the Wi-Fi password in plain text**: keep it on the USB stick, and don't email it or add it to the repo (this folder's `.gitignore` ignores `.xml` files).

Copy `switch-wifi.cmd` onto the stick next to it. Keep just the one `.xml` file there.

## On each laptop

Plug in the stick and double-click `switch-wifi.cmd`. It shows the network, connects, and then prints the laptop's Wi-Fi state and network name. To see which network it would use without changing anything, run `switch-wifi.cmd /check` from Command Prompt.

- The network is saved for **whoever is signed in**, so no administrator rights are needed. If students sign in with their own accounts, run it in their account, not yours.
- Windows remembers the network, so a laptop rejoins it by itself next time it's in range.
- School-managed laptops may block adding Wi-Fi networks. Then the script says Windows didn't accept the network, and only the school's IT can change it.
