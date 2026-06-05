#!/usr/bin/env bash
#
# GuardSwitch – Linux-Installer
#
# Aus dem entpackten Tarball heraus ausführen:
#   sudo ./install.sh
#
# Installiert den Service nach /opt/guardswitch, richtet die systemd-Unit ein
# und legt den Tray-Autostart für deinen Desktop-User an.
#
set -euo pipefail

INSTALL_DIR=/opt/guardswitch
CONFIG_DIR=/etc/guardswitch
UNIT_DST=/etc/systemd/system/guardswitch.service

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Der Installer liegt unter packaging/linux/, die Binaries im Tarball-Root.
# Beides unterstützen: Aufruf aus dem Tarball-Root oder aus packaging/linux/.
if [[ -f "$SCRIPT_DIR/WgAutoswitch.Service" ]]; then
  ROOT_DIR="$SCRIPT_DIR"
elif [[ -f "$SCRIPT_DIR/../../WgAutoswitch.Service" ]]; then
  ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
else
  echo "FEHLER: WgAutoswitch.Service nicht gefunden. Bitte aus dem entpackten" >&2
  echo "        Tarball-Verzeichnis ausführen." >&2
  exit 1
fi

PKG_DIR="$ROOT_DIR/packaging/linux"

if [[ $EUID -ne 0 ]]; then
  echo "Bitte mit root-Rechten ausführen:  sudo ./install.sh" >&2
  exit 1
fi

# Ziel-User für den Tray-Autostart (der User, der sudo aufgerufen hat).
TARGET_USER="${SUDO_USER:-}"
if [[ -z "$TARGET_USER" || "$TARGET_USER" == "root" ]]; then
  echo "WARNUNG: Kein normaler Desktop-User erkennbar (SUDO_USER leer)." >&2
  echo "         Service wird installiert, der Tray-Autostart aber übersprungen." >&2
  echo "         Tray später manuell einrichten (siehe LINUX.md)." >&2
  TARGET_USER=""
fi

# --- Voraussetzungen prüfen ------------------------------------------------
if ! command -v nmcli >/dev/null 2>&1; then
  echo "WARNUNG: nmcli (NetworkManager) nicht gefunden. GuardSwitch steuert den" >&2
  echo "         WireGuard-Tunnel über NetworkManager – ohne ihn funktioniert es nicht." >&2
fi

echo "==> Installiere nach $INSTALL_DIR"
install -d -m 0755 "$INSTALL_DIR" "$CONFIG_DIR"

# Laufenden Dienst stoppen, bevor die Binary ersetzt wird (Update-Fall).
if systemctl is-active --quiet guardswitch 2>/dev/null; then
  echo "==> Stoppe laufenden guardswitch-Dienst"
  systemctl stop guardswitch
fi

install -m 0755 "$ROOT_DIR/WgAutoswitch.Service"  "$INSTALL_DIR/WgAutoswitch.Service"
install -m 0755 "$ROOT_DIR/WgAutoswitch.LinuxTray" "$INSTALL_DIR/WgAutoswitch.LinuxTray"

echo "==> Richte systemd-Unit ein"
install -m 0644 "$PKG_DIR/guardswitch.service" "$UNIT_DST"
systemctl daemon-reload
systemctl enable --now guardswitch

# --- Tray-Autostart für den Desktop-User -----------------------------------
if [[ -n "$TARGET_USER" ]]; then
  USER_HOME="$(getent passwd "$TARGET_USER" | cut -d: -f6)"
  if [[ -n "$USER_HOME" && -d "$USER_HOME" ]]; then
    AUTOSTART_DIR="$USER_HOME/.config/autostart"
    echo "==> Richte Tray-Autostart für $TARGET_USER ein"
    install -d -m 0755 "$AUTOSTART_DIR"
    install -m 0644 "$PKG_DIR/guardswitch-tray.desktop" "$AUTOSTART_DIR/guardswitch-tray.desktop"
    chown -R "$TARGET_USER":"$(id -gn "$TARGET_USER")" "$USER_HOME/.config/autostart"
  fi
fi

echo
echo "Fertig. Service-Status:"
systemctl --no-pager --lines=0 status guardswitch || true

cat <<EOF

Nächste Schritte
----------------
1. WireGuard-Profil in NetworkManager importieren (falls noch nicht geschehen):
     nmcli connection import type wireguard file ~/Downloads/home.conf
     nmcli connection show

2. Tunnel-Name in $CONFIG_DIR/config.toml an den NetworkManager-Verbindungsnamen
   anpassen und Heim-Erkennung (gateway_mac / ssid / reachable_host) eintragen.
   Danach:  sudo systemctl restart guardswitch

3. Den Tray jetzt starten (oder einfach neu einloggen):
     /opt/guardswitch/WgAutoswitch.LinuxTray &

Logs:  journalctl -u guardswitch -f
EOF
