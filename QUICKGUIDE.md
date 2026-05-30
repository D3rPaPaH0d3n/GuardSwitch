# GuardSwitch - Quickguide

Dieses Programm schaltet deinen WireGuard-VPN-Tunnel **automatisch ein**,
wenn du unterwegs bist, und **automatisch aus**, sobald du wieder zuhause
bist. Du musst nichts mehr manuell ein- oder ausschalten.

---

## Was du brauchst

Bevor du loslegst, sollte folgendes vorbereitet sein:

### 1. WireGuard für Windows ist installiert

Falls noch nicht: kostenlos hier herunterladen und installieren:
**https://www.wireguard.com/install/**

### 2. Du hast bereits einen WireGuard-Tunnel angelegt

In WireGuard auf "Tunnel hinzufügen" geklickt, deine Konfiguration
importiert, und manuell einmal getestet, dass der Tunnel funktioniert.

> Den **Namen deines Tunnels** brauchst du gleich. Er steht in der linken
> Liste in der WireGuard-App. Beispiel: `home`, `office`, `mein-vpn`.

### 3. Die MAC-Adresse deines Routers

Das klingt technisch, ist aber in 30 Sekunden erledigt:

1. Klicke auf **Start**, tippe `cmd` ein, drücke Enter (Eingabeaufforderung
   öffnet sich)
2. Tippe folgendes ein und drücke Enter:
   ```
   arp -a
   ```
3. In der Liste suchst du die IP-Adresse deines Routers. Das ist die,
   die wahrscheinlich auf `.1` endet (z.B. `192.168.178.1` bei FritzBox)
4. **Daneben** steht die "Physische Adresse" - das ist die MAC, sieht
   so aus: `aa-bb-cc-dd-ee-ff`
5. **Diese MAC notieren** - du brauchst sie gleich.

> **Warum die MAC und nicht der WLAN-Name?** Weil ein WLAN-Name leicht
> woanders auch existieren kann (Cafe heißt zufällig genauso wie dein
> Heimnetz). Die MAC deines Routers ist eindeutig.

### 4. Optional: Eine IP, die nur zuhause erreichbar ist

Wenn du sowas wie ein NAS, einen Raspberry Pi oder einen Heimserver hast:
seine IP-Adresse aufschreiben (z.B. `192.168.178.5`). Damit kann
GuardSwitch *zusätzlich* prüfen, ob du wirklich zuhause bist.

Wenn du sowas nicht hast: einfach leer lassen, MAC + WLAN reichen auch.

---

## Installation

1. **`guardswitch-setup-1.0.0.exe`** doppelklicken
2. Falls Windows-Schutz fragt: **"Weitere Informationen"** → **"Trotzdem ausführen"**
3. Bei "Möchten Sie Änderungen zulassen?" auf **Ja** klicken
4. Im Wizard durchklicken:
   - Sprache wählen
   - Voraussetzungen-Hinweis lesen
   - Installationsort: einfach belassen (`C:\Program Files\guardswitch`)
   - Häkchen bei **"Tray-Symbol bei Windows-Anmeldung automatisch starten"** lassen
5. **Konfigurationsseite ausfüllen:**
   - **Tunnelname:** genau wie in WireGuard, z.B. `home`
   - **Router-MAC:** die vorher notierte MAC
   - **WLAN-Name:** dein Heim-WLAN-Name (optional)
   - **IP eines Heim-Geräts:** dein NAS/Pi (optional)
   - **Port:** Standard `22` lassen (oder `53` für DNS-Server, `80` für Webdienste)
6. **Installieren** klicken
7. Nach Abschluss: Häkchen bei **"Tray-Symbol jetzt starten"** lassen → Fertig

---

## Nutzung im Alltag

### Das Tray-Symbol verstehen

Unten rechts in der Taskleiste (Pfeil nach oben → kleiner Kreis):

| Farbe | Bedeutung |
|-------|-----------|
| 🟢 Grün | Du bist zuhause - VPN ist **aus** (alles okay) |
| 🔵 Blau | Du bist unterwegs - VPN ist **an** (alles okay) |
| ⚪ Grau | Auto-Modus pausiert - VPN macht nichts automatisch |
| 🔴 Rot | Fehler - meistens läuft der Hintergrunddienst nicht |

Wenn du mit der Maus drüberfährst, siehst du den genauen Status als Tooltip.

### Rechtsklick auf das Symbol

Hier hast du alle Optionen:

- **Status-Zeile oben** zeigt, was gerade los ist und warum
- **Auto-Modus pausieren** ← der Notausschalter, falls etwas schiefgeht
- **Tunnel "home"** → Ausklappen für manuelles Ein/Aus
- **Konfiguration neu laden** - nach Änderungen an der Config-Datei
- **Konfiguration öffnen** - öffnet die TOML-Datei in Notepad
- **Log öffnen** - zeigt, was das Programm im Hintergrund tut

### Wenn etwas nicht funktioniert: 4 Stufen Notausschalter

**Stufe 1 - Auto-Modus pausieren**
Rechtsklick auf das Tray-Symbol → "Auto-Modus pausieren". Das Programm
lässt deinen Tunnel ab sofort in Ruhe. Du kannst trotzdem manuell
ein- und ausschalten.

**Stufe 2 - Tray beenden**
Rechtsklick → "Tray beenden". Der Hintergrunddienst läuft weiter, du
siehst nur das Symbol nicht mehr.

**Stufe 3 - Hintergrunddienst stoppen**
- `Win + R` drücken
- `services.msc` eingeben, Enter
- "GuardSwitch" suchen → Rechtsklick → Beenden
- Für dauerhaft: zusätzlich Doppelklick → Starttyp auf "Deaktiviert"

**Stufe 4 - Komplett deinstallieren**
Windows-Einstellungen → Apps → "GuardSwitch" → Deinstallieren.
Danach ist alles weg, dein WireGuard selbst bleibt unangetastet.

---

## Konfiguration anpassen

Falls du später Werte ändern willst (z.B. neuer Router → neue MAC):

1. Rechtsklick aufs Tray-Symbol → **Konfiguration öffnen**
2. Notepad öffnet die Datei. Werte anpassen.
3. Speichern (Strg+S) und Notepad schließen
4. Rechtsklick aufs Tray-Symbol → **Konfiguration neu laden**

Erklärung der Werte:

```toml
[general]
enabled = true                    # false = komplett deaktiviert
check_interval_seconds = 10       # alle X Sekunden prüfen
hysteresis_count = 2              # 2x in Folge gleiche Erkennung vor Wechsel
                                  # (verhindert Flackern bei kurzen Aussetzern)
min_checks_required = 2           # Mindestens 2 Indikatoren müssen
                                  # "zuhause" sagen, sonst wird Tunnel
                                  # vorsichtshalber aktiviert

[[tunnels]]
name = "home"                     # Name des WireGuard-Tunnels

[home_detection]
gateway_mac = "aa-bb-cc-dd-ee-ff" # MAC des Routers
ssid = "MeinWLAN"                 # WLAN-Name
reachable_host = "192.168.178.5"  # IP eines Heim-Geräts
reachable_port = 22               # Port darauf
```

---

## Häufige Probleme

**Tray-Symbol zeigt rot ("Service nicht erreichbar")**
Der Hintergrunddienst läuft nicht. Computer neu starten, oder über
`services.msc` den Dienst "GuardSwitch" manuell starten.

**Tray-Symbol bleibt grau / kein automatischer Wechsel**
Auto-Modus ist pausiert. Rechtsklick → "Auto-Modus aktivieren".

**Das Programm denkt fälschlich, ich sei zuhause / nicht zuhause**
Status anschauen (Rechtsklick → erste Menüzeile zeigt die Begründung).
Häufige Ursachen:
- MAC-Adresse falsch eingetragen
- WLAN-Name leicht anders geschrieben (Groß-/Kleinschreibung beachten!)
- Gerät unter `reachable_host` ist gerade ausgeschaltet

**Tunnel-Name falsch geschrieben**
Im Status steht dann "NotInstalled". Prüfen: in WireGuard nachsehen, wie
der Tunnel exakt heißt, dann in der Config korrigieren und neu laden.

**Ich will doch alles wegmachen**
Windows-Einstellungen → Apps → "GuardSwitch" → Deinstallieren.
Bei der Frage "Konfigurationsdatei behalten?" auf **Nein** klicken.
WireGuard selbst bleibt installiert und unverändert.

---

## Wo finde ich was?

| Was | Wo |
|-----|-----|
| Programm-Dateien | `C:\Program Files\guardswitch\` |
| Konfiguration | `C:\ProgramData\guardswitch\config.toml` |
| Log-Datei | `C:\ProgramData\guardswitch\log.txt` |
| Detail-Logs | Ereignisanzeige → Anwendung → Quelle "guardswitch" |

> **Tipp:** Den `ProgramData`-Ordner siehst du im Explorer normalerweise nicht.
> Du kannst die Pfade aber direkt in die Adresszeile eintippen, oder einfach
> aus dem Tray-Menü "Konfiguration öffnen" und "Log öffnen" verwenden.
