# GuardSwitch

<sub>(technischer Bezeichner / Repo-Name: `wg-autoswitch` — Dienst, Pipe und Config-Ordner heißen weiterhin so)</sub>

Automatisches Aktivieren/Deaktivieren eines WireGuard-Tunnels unter Windows
basierend auf der Erkennung des Heimnetzwerks. Mit Tray-Icon zur Statusanzeige
und Pause-Funktion als Notausschalter.

Schließt eine Lücke des offiziellen WireGuard-Clients für Windows: die
"Trusted-Networks"-Funktion gibt es nur unter iOS/macOS, nicht unter Windows.
Diese App erkennt das Heimnetz lokal anhand von SSID, Router-MAC oder einem
Heim-Gerät - ohne Cloud, ohne externen Dienst.

## Download

Aktuelle Version → [**Releases**](https://github.com/D3rPaPaH0d3n/wg-autoswitch/releases/latest)

`wg-autoswitch-setup-X.Y.Z.exe` herunterladen und ausführen. Dabei:

> ⚠️ Windows zeigt "Unbekannter Herausgeber" / SmartScreen-Warnung, weil der
> Installer nicht code-signed ist (Zertifikate kosten Geld, die App ist
> kostenlos). Auf "Weitere Infos" → "Trotzdem ausführen" klicken.

Voraussetzung: [WireGuard für Windows](https://www.wireguard.com/install/) ist
bereits installiert und mindestens ein Tunnel ist eingerichtet.

### Updates

Einfach den neuen Installer drüberlaufen lassen — kein Deinstallieren nötig.
Service wird automatisch gestoppt, getauscht und wieder gestartet, deine
Konfiguration unter `C:\ProgramData\wg-autoswitch\config.toml` bleibt erhalten.
Konfig anpassen geht jederzeit über das Tray-Menü → "Konfiguration öffnen".

## Aufbau

- **WgAutoswitch.Service** - Windows-Dienst (LocalSystem), macht die eigentliche Arbeit
- **WgAutoswitch.Tray** - User-Tray-App, zeigt Status und steuert per Named Pipe
- **WgAutoswitch.Shared** - Geteilte Modelle und IPC-Protokoll

## Build

Drei Wege:

**A) Cloud-Build via GitHub Actions** (keine lokale Installation nötig) →
siehe [CLOUD-BUILD.md](CLOUD-BUILD.md)

**B) Lokaler Schnellweg: alles inkl. Installer**

Voraussetzungen:
- .NET 8 SDK ([dot.net](https://dot.net))
- Inno Setup 6 ([jrsoftware.org/isdl.php](https://jrsoftware.org/isdl.php))

Dann einfach:
```powershell
.\build.bat
```

Ergebnis: `installer\output\wg-autoswitch-setup-1.0.0.exe`

Das ist die Datei, die du an Endnutzer weitergibst.

### Nur Code bauen, kein Installer (lokal)

```powershell
dotnet build -c Release WgAutoswitch.sln
```

### Self-contained Publish (was der Installer braucht)

```powershell
dotnet publish src\WgAutoswitch.Service -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
dotnet publish src\WgAutoswitch.Tray -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Installation

**Für Endnutzer:** Den Installer `wg-autoswitch-setup-1.0.0.exe` ausführen
und der Anleitung im [QUICKGUIDE.md](QUICKGUIDE.md) folgen.

**Manuell (für Entwicklung):**

Vorbedingung: Der WireGuard-Tunnel muss schon in WireGuard für Windows
importiert sein, der Service `WireGuardTunnel$<name>` muss existieren.

### Service installieren

Als Admin in PowerShell:

```powershell
sc create wg-autoswitch binPath= "C:\Tools\wg-autoswitch\WgAutoswitch.Service.exe" `
    DisplayName= "GuardSwitch" `
    start= auto
sc description wg-autoswitch "Aktiviert/deaktiviert WireGuard-Tunnel je nach Netzwerk."
sc start wg-autoswitch
```

### Tray automatisch starten

Verknüpfung von `WgAutoswitch.Tray.exe` in den Autostart legen:
```
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
```

## Konfiguration

Beim ersten Start des Service wird `C:\ProgramData\wg-autoswitch\config.toml`
mit Default-Werten erzeugt. Anpassen:

```toml
[general]
enabled = true
check_interval_seconds = 10
hysteresis_count = 2          # Wieviele aufeinanderfolgende gleiche Ergebnisse vor Wechsel
min_checks_required = 2       # Mindestens N positive Checks für "zuhause"

[[tunnels]]
name = "home"                 # Entspricht dem Service WireGuardTunnel$home

[home_detection]
gateway_mac = "AA:BB:CC:DD:EE:FF"  # MAC der FritzBox 5690 Pro
ssid = "DeinWLAN"
reachable_host = "192.168.178.5"   # Pi DNS-Master
reachable_port = 53
```

Nach Änderungen entweder Service neustarten oder im Tray
"Konfiguration neu laden" klicken.

## Notausschalter (mehrere Ebenen)

| Ebene | Was passiert | Wie |
|---|---|---|
| 1 | Auto-Modus pausieren | Tray-Rechtsklick → "Auto-Modus pausieren" |
| 2 | Service stoppen | `sc stop wg-autoswitch` |
| 3 | Service deaktivieren | `sc config wg-autoswitch start= disabled` |
| 4 | Komplett weg | `sc delete wg-autoswitch` |

WireGuard selbst wird nie verändert. Wenn der Service weg ist, bleibt
alles wie zuletzt - kein "Panik-Aus".

## Logs

- Windows Event Log: Anwendung, Quelle "wg-autoswitch"
- `C:\ProgramData\wg-autoswitch\log.txt` (Service schreibt mit, bei 1 MB
  Rotation nach `log.txt.old`)

## Tray-Icon-Farben

- 🟢 Grün - zuhause erkannt, Tunnel aus
- 🔵 Blau - unterwegs erkannt, Tunnel an
- ⚪ Grau - Auto-Modus pausiert
- 🔴 Rot - Service nicht erreichbar oder Fehler

## TODO / Erweiterungen

- Optional: Settings-Dialog statt nur Notepad
- Optional: Heim-Erkennung über Cloudflare-Tunnel-Status
- Optional: per-Tunnel-Konfiguration (verschiedene Tunnel für verschiedene Netze)
