# Bauen ohne lokale Installation (GitHub Actions)

Wenn du dir keine Build-Umgebung (.NET SDK + Inno Setup) lokal installieren
willst, kannst du den Build komplett in der Cloud laufen lassen. Du brauchst
nur einen GitHub-Account.

## Vorteile

- ✅ Keine lokale Installation nötig
- ✅ Frische, saubere Build-Umgebung jedes Mal
- ✅ Kostenlos (für öffentliche Repos unbegrenzt, für private 2000 min/Monat)
- ✅ Automatisches Versionieren via Git-Tags
- ✅ Automatische GitHub-Releases bei Tags

## Einmaliges Setup (5 Minuten)

### 1. GitHub-Repo erstellen

1. Auf https://github.com einloggen
2. Oben rechts auf **+** → **New repository**
3. Name: z.B. `guardswitch`
4. Private oder Public - egal, für deine Zwecke reicht Private
5. **NICHT** "Initialize with README" anhaken (du hast schon Dateien)
6. **Create repository** klicken

### 2. Lokal das Projekt nach GitHub pushen

Im Projektordner (PowerShell):

```powershell
git init
git add .
git commit -m "Initial commit"
git branch -M main
git remote add origin https://github.com/DEIN-USERNAME/guardswitch.git
git push -u origin main
```

> Falls du noch nie mit Git gearbeitet hast: GitHub Desktop ist die einfachste
> Variante - https://desktop.github.com - du klickst dort einfach "Add Local
> Repository", "Publish to GitHub", fertig. Kein Kommandozeilen-Voodoo.

### 3. Workflow-Berechtigung prüfen

Der Workflow braucht Schreibrechte um Releases zu erstellen:

1. Im Repo auf **Settings** klicken (oben in der Navigation)
2. Links **Actions** → **General**
3. Ganz unten **"Workflow permissions"**
4. **"Read and write permissions"** auswählen
5. Speichern

Das wars an Setup. Ab jetzt baut GitHub bei jedem Push automatisch.

## Build auslösen und Installer herunterladen

### Variante A: Manueller Build (jederzeit)

1. Im Repo auf **Actions** klicken
2. Links **"Build GuardSwitch"** anklicken
3. Rechts **"Run workflow"** Button → **"Run workflow"** bestätigen
4. Warten (~3-5 Minuten)
5. Auf den fertigen Run klicken
6. Ganz unten unter **"Artifacts"** liegt `guardswitch-installer` zum Download
7. ZIP herunterladen, entpacken → `guardswitch-setup-1.0.0.exe` ist drin

### Variante B: Automatischer Build mit Release (bei neuen Versionen)

Wenn du einen Git-Tag pushst, der mit `v` anfängt, wird automatisch ein
GitHub-Release mit dem Installer erstellt:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Nach ein paar Minuten findest du unter **Releases** im Repo den Installer
zum direkten Download. Diesen Link kannst du auch an andere Leute weitergeben.

## Was wenn der Build fehlschlägt?

1. Im **Actions**-Tab auf den fehlgeschlagenen Run klicken
2. Auf den Job **"build"** klicken
3. Den roten Schritt aufklappen → die Fehlermeldung steht da

Häufige Probleme:
- **"You need write access"** → Workflow-Berechtigung wie oben in Schritt 3 prüfen
- **NuGet-Paket nicht gefunden** → Cache löschen: Actions → Caches → alle löschen
- **Inno Setup Fehler** → meist ein Tippfehler in der `.iss`-Datei

## Cloud-Build vs. lokaler Build

| | Cloud (GitHub Actions) | Lokal (build.bat) |
|---|---|---|
| Setup-Aufwand | 5 Min einmalig | 10 Min (.NET SDK + Inno Setup laden) |
| Build-Dauer | 3-5 Min | 30-60 Sek |
| Internet nötig | Ja | Nur fürs initiale Setup |
| Saubere Umgebung | Immer | Hängt vom Rechner ab |
| Code-Änderungen testen | Pushen nötig | Sofort |

**Empfehlung:** Für die initiale Version und Releases: Cloud. Für aktive
Entwicklung mit vielen kleinen Änderungen: lokal schneller.
