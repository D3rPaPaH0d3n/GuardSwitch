# GuardSwitch on Linux

GuardSwitch uses NetworkManager on Linux. Import your WireGuard profile first:

```bash
nmcli connection import type wireguard file ~/Downloads/home.conf
nmcli connection show
```

The `[[tunnels]].name` value in `/etc/guardswitch/config.toml` must match the
NetworkManager connection name.

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
