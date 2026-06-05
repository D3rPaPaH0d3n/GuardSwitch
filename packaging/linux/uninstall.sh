#!/usr/bin/env bash
#
# GuardSwitch – Linux-Deinstaller
#
#   sudo ./uninstall.sh            entfernt Service + Binaries, behält /etc/guardswitch
#   sudo ./uninstall.sh --purge    zusätzlich Config /etc/guardswitch und Log entfernen
#
set -euo pipefail

INSTALL_DIR=/opt/guardswitch
CONFIG_DIR=/etc/guardswitch
UNIT_DST=/etc/systemd/system/guardswitch.service
LOG_FILE=/var/log/guardswitch.log

PURGE=0
[[ "${1:-}" == "--purge" ]] && PURGE=1

if [[ $EUID -ne 0 ]]; then
  echo "Bitte mit root-Rechten ausführen:  sudo ./uninstall.sh" >&2
  exit 1
fi

echo "==> Stoppe und deaktiviere Dienst"
systemctl disable --now guardswitch 2>/dev/null || true
rm -f "$UNIT_DST"
systemctl daemon-reload

echo "==> Entferne Binaries ($INSTALL_DIR)"
rm -rf "$INSTALL_DIR"

# Tray-Autostart beim aufrufenden User entfernen.
TARGET_USER="${SUDO_USER:-}"
if [[ -n "$TARGET_USER" && "$TARGET_USER" != "root" ]]; then
  USER_HOME="$(getent passwd "$TARGET_USER" | cut -d: -f6)"
  rm -f "$USER_HOME/.config/autostart/guardswitch-tray.desktop"
fi

if [[ $PURGE -eq 1 ]]; then
  echo "==> Entferne Konfiguration und Log"
  rm -rf "$CONFIG_DIR"
  rm -f "$LOG_FILE"
else
  echo "Konfiguration unter $CONFIG_DIR bleibt erhalten (--purge zum Löschen)."
fi

echo "Fertig."
