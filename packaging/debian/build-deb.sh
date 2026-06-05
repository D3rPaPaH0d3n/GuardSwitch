#!/usr/bin/env bash
#
# Baut das GuardSwitch .deb-Paket aus zwei bereits veröffentlichten Binaries.
#
# Aufruf:
#   build-deb.sh <version> <service-binary> <tray-binary> [out-dir]
#
# Beispiel (nach dotnet publish -r linux-x64):
#   packaging/debian/build-deb.sh 2.0.1 \
#     src/WgAutoswitch.Service/bin/Release/net8.0/linux-x64/publish/WgAutoswitch.Service \
#     src/WgAutoswitch.LinuxTray/bin/Release/net8.0/linux-x64/publish/WgAutoswitch.LinuxTray \
#     dist
#
set -euo pipefail

if [[ $# -lt 3 ]]; then
  echo "Aufruf: $0 <version> <service-binary> <tray-binary> [out-dir]" >&2
  exit 2
fi

VERSION="$1"
SERVICE_BIN="$2"
TRAY_BIN="$3"
OUT_DIR="${4:-.}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

for f in "$SERVICE_BIN" "$TRAY_BIN"; do
  [[ -f "$f" ]] || { echo "FEHLER: Binary nicht gefunden: $f" >&2; exit 1; }
done

PKG="$(mktemp -d)"
trap 'rm -rf "$PKG"' EXIT
chmod 0755 "$PKG"   # mktemp legt 0700 an – Paketwurzel soll 0755 sein

# --- Dateibaum aufbauen (so, wie es auf dem Zielsystem liegen soll) ---------
install -d -m 0755 "$PKG/DEBIAN" \
                   "$PKG/opt/guardswitch" \
                   "$PKG/usr/lib/systemd/system" \
                   "$PKG/etc/xdg/autostart"

install -m 0755 "$SERVICE_BIN" "$PKG/opt/guardswitch/WgAutoswitch.Service"
install -m 0755 "$TRAY_BIN"    "$PKG/opt/guardswitch/WgAutoswitch.LinuxTray"
install -m 0644 "$REPO_ROOT/packaging/linux/guardswitch.service" \
                "$PKG/usr/lib/systemd/system/guardswitch.service"
# Systemweiter Tray-Autostart – gilt für jeden grafisch angemeldeten User,
# ohne SUDO_USER-Gefummel wie beim Tarball-Installer.
install -m 0644 "$REPO_ROOT/packaging/linux/guardswitch-tray.desktop" \
                "$PKG/etc/xdg/autostart/guardswitch-tray.desktop"

# --- DEBIAN-Metadaten -------------------------------------------------------
INSTALLED_KB="$(du -k -s "$PKG" | cut -f1)"
sed -e "s/@VERSION@/$VERSION/" -e "s/@INSTALLED_SIZE@/$INSTALLED_KB/" \
    "$SCRIPT_DIR/control" > "$PKG/DEBIAN/control"

install -m 0755 "$SCRIPT_DIR/postinst" "$PKG/DEBIAN/postinst"
install -m 0755 "$SCRIPT_DIR/prerm"    "$PKG/DEBIAN/prerm"
install -m 0755 "$SCRIPT_DIR/postrm"   "$PKG/DEBIAN/postrm"

mkdir -p "$OUT_DIR"
DEB="$OUT_DIR/guardswitch_${VERSION}_amd64.deb"

# --root-owner-group: Dateien gehören im Archiv root:root, nicht dem Build-User.
dpkg-deb --root-owner-group --build "$PKG" "$DEB" >/dev/null

echo "$DEB"
