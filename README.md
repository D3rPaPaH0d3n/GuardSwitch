<div align="center">

# 🛡️ GuardSwitch

### Dein WireGuard-VPN schaltet sich selbst ein – immer dann, wenn du es brauchst.

**Zuhause aus. Unterwegs an. Vollautomatisch.**

[![Download](https://img.shields.io/badge/⬇_Download-0078D4?style=for-the-badge)](https://github.com/D3rPaPaH0d3n/wg-autoswitch/releases/latest)
[![Plattform](https://img.shields.io/badge/Windows_10/11-0078D4?style=for-the-badge&logo=windows&logoColor=white)](#)
[![Spenden](https://img.shields.io/badge/💚_Spenden-FF5F5F?style=for-the-badge)](https://revolut.me/mkainer/pocket/QAt1Q0Ntsb)

<sub>Technischer Bezeichner / Repo-Name: `wg-autoswitch` — Dienst, Pipe und Config-Ordner heißen weiterhin so.</sub>

</div>

---

## Was ist GuardSwitch?

GuardSwitch ist ein kleiner, ressourcenschonender Windows-Begleiter für
[WireGuard](https://www.wireguard.com/). Er erkennt automatisch, ob du **zuhause**
oder **unterwegs** bist, und schaltet deinen VPN-Tunnel entsprechend ein oder aus –
ganz ohne dass du daran denken musst.

> 💡 **Warum überhaupt?** Der offizielle WireGuard-Client für Windows hat keine
> „Trusted Networks"-Funktion – die gibt es nur auf iOS und macOS. GuardSwitch
> schließt genau diese Lücke. Die Heimnetz-Erkennung passiert **komplett lokal**
> anhand von WLAN-Name, Router-MAC oder einem Gerät in deinem Netz – **keine Cloud,
> kein externer Dienst, keine Daten verlassen deinen PC.**

## ✨ Features

- 🔄 **Automatisches Umschalten** – Tunnel an unterwegs, aus zuhause. Du machst nichts.
- 🏠 **Zuverlässige Heimerkennung** – über WLAN-SSID, Router-MAC und/oder erreichbares
  Heim-Gerät. Mehrere Kriterien kombinierbar für maximale Treffsicherheit.
- 🧠 **Stabil statt nervös** – Settle-Delay nach Netzwerkwechseln, Wiederholungs-Checks
  und asymmetrische Hysterese (schnell schützen, ruhig abschalten) verhindern
  Fehlschaltungen und Flackern.
- 🔒 **Privat by Design** – alles läuft offline auf deinem Rechner.
- 🛡️ **Modernes Tray-Icon** – Fluent-Schild, passt sich an helle/dunkle Taskleiste an,
  Status auf einen Blick an Farbe **und** Symbol erkennbar.
- 🛑 **Notausschalter** – Auto-Modus jederzeit per Tray-Klick pausieren. WireGuard selbst
  wird dabei nie verändert.
- 📦 **Einfache Installation & Updates** – ein Installer, Updates laufen einfach drüber,
  deine Konfiguration bleibt erhalten.

## 🚀 So funktioniert's

1. **Erkennen** – GuardSwitch prüft im Hintergrund Merkmale deines aktuellen Netzes
   (WLAN-Name, Router-MAC, ein Heim-Gerät).
2. **Entscheiden** – Stimmen genug Merkmale → „zuhause". Sonst → „unterwegs".
   Eine Hysterese sorgt dafür, dass kurze Schwankungen nichts auslösen.
3. **Schalten** – Zuhause wird der Tunnel deaktiviert, unterwegs aktiviert –
   über den ganz normalen WireGuard-Dienst.

Den Status siehst du jederzeit am Tray-Icon. Fertig.

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
settle_delay_seconds = 3        # Nach Netzwerkwechsel kurz warten, bevor gemessen wird
check_retries = 2               # Wackelige Checks (ARP/Erreichbarkeit) mehrfach versuchen
hysteresis_count_home = 3       # So oft "zuhause" in Folge, bevor der Tunnel AUS geht (ruhig)
hysteresis_count_away = 1       # So oft "unterwegs" in Folge, bevor der Tunnel AN geht (schnell)
min_checks_required = 1         # Mindestens N positive Checks für "zuhause"

[[tunnels]]
name = "home"                   # Entspricht dem Service WireGuardTunnel$home

[home_detection]
gateway_mac = "AA:BB:CC:DD:EE:FF"  # MAC deines Routers (z. B. FritzBox)
ssid = "DeinWLAN"                  # Name deines Heim-WLANs
reachable_host = "192.168.178.5"   # Ein Gerät, das es nur zuhause gibt (z. B. NAS/Pi)
reachable_port = 53
```

> 💡 Du musst nicht alle drei Kriterien setzen – eines reicht. Mehrere kombiniert
> machen die Erkennung aber treffsicherer.

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

## Tray-Icon

Schild-Symbol im Fluent-Stil, passt sich an helle/dunkle Taskleiste an. Der
Zustand ist an Farbe **und** Innensymbol erkennbar:

- 🛡 Grün + Häkchen - zuhause erkannt, Tunnel aus
- 🛡 Blau + Schloss - unterwegs erkannt, Tunnel an
- 🛡 Grau + Pause - Auto-Modus pausiert
- 🛡 Rot + Ausrufezeichen - Service nicht erreichbar oder Fehler
- 🛡 Grau + Fragezeichen - Zustand noch unbekannt

## TODO / Erweiterungen

- Optional: Settings-Dialog statt nur Notepad
- Optional: Heim-Erkennung über Cloudflare-Tunnel-Status
- Optional: per-Tunnel-Konfiguration (verschiedene Tunnel für verschiedene Netze)

## Unterstützen

GuardSwitch ist kostenlos und Open Source. Wenn dir das Projekt hilft, freue ich
mich über eine kleine Spende:

[![Spenden](https://img.shields.io/badge/💚_Spenden-FF5F5F?style=for-the-badge)](https://revolut.me/mkainer/pocket/QAt1Q0Ntsb)
