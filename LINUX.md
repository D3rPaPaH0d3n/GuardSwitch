# GuardSwitch on Linux

GuardSwitch uses NetworkManager on Linux. Import your WireGuard profile first:

```bash
nmcli connection import type wireguard file ~/Downloads/home.conf
nmcli connection show
```

The `[[tunnels]].name` value in `/etc/guardswitch/config.toml` must match the
NetworkManager connection name.

## Install on Linux Mint / Ubuntu / Debian (.deb, recommended)

Download `guardswitch_<version>_amd64.deb` from the Releases page and
**double-click it** – the graphical package installer opens, you click
*Install*, enter your password, done. It registers and starts the systemd
service and sets up the tray autostart automatically.

From the terminal it's:

```bash
sudo apt install ./guardswitch_2.0.1_amd64.deb
```

Uninstall via the Software Manager, or `sudo apt remove guardswitch`
(`sudo apt purge guardswitch` also removes the config).

Afterwards import your WireGuard profile (see above), adjust
`/etc/guardswitch/config.toml` and run `sudo systemctl restart guardswitch`.

## Install from the release tarball

Download `guardswitch-linux-x64.tar.gz` from the Releases page, extract it and
run the installer:

```bash
tar xzf guardswitch-linux-x64.tar.gz
cd guardswitch-linux-x64
sudo ./install.sh
```

The installer copies the binaries to `/opt/guardswitch`, enables the systemd
service and sets up the tray autostart for your desktop user. Then import your
WireGuard profile (see above), adjust `/etc/guardswitch/config.toml` and run
`sudo systemctl restart guardswitch`.

Uninstall with `sudo ./uninstall.sh` (add `--purge` to also remove the config).

## Manual development install

Publish the service and tray app:

```bash
dotnet publish src/WgAutoswitch.Service -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
dotnet publish src/WgAutoswitch.LinuxTray -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

Install:

```bash
sudo mkdir -p /opt/guardswitch /etc/guardswitch
sudo cp src/WgAutoswitch.Service/bin/Release/net8.0/linux-x64/publish/WgAutoswitch.Service /opt/guardswitch/
sudo cp src/WgAutoswitch.LinuxTray/bin/Release/net8.0/linux-x64/publish/WgAutoswitch.LinuxTray /opt/guardswitch/
sudo cp packaging/linux/guardswitch.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now guardswitch
```

Start the visual control app from the desktop autostart file:

```bash
mkdir -p ~/.config/autostart
cp packaging/linux/guardswitch-tray.desktop ~/.config/autostart/
/opt/guardswitch/WgAutoswitch.LinuxTray &
```

Logs:

```bash
journalctl -u guardswitch -f
tail -f /var/log/guardswitch.log
```
