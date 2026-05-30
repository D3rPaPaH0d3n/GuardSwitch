; ===========================================================
; wg-autoswitch Installer
; Inno Setup Skript - benötigt Inno Setup 6.2 oder neuer
; https://jrsoftware.org/isdl.php
; ===========================================================

#define MyAppName "GuardSwitch"
#define MyAppShortName "wg-autoswitch"
; MyAppVersion kann via Kommandozeile überschrieben werden:
;   ISCC.exe /DMyAppVersion=1.2.3 wg-autoswitch.iss
; Wird im CI-Workflow aus dem Git-Tag (v1.2.3) abgeleitet.
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif
#define MyAppPublisher "Markus Kainer (@D3rPaPaH0d3n)"
#define MyAppURL "https://github.com/D3rPaPaH0d3n/wg-autoswitch"
#define MyServiceName "wg-autoswitch"
#define MyServiceExe "WgAutoswitch.Service.exe"
#define MyTrayExe "WgAutoswitch.Tray.exe"

[Setup]
AppId={{8B7E2F4A-9C3D-4E5B-A1F2-3D4E5F6A7B8C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=wg-autoswitch-setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyTrayExe}
SetupIconFile=..\src\WgAutoswitch.Tray\app.ico
SetupLogging=yes
CloseApplications=force

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Tray-Symbol bei Windows-Anmeldung automatisch starten"; GroupDescription: "Verknüpfungen:"; Flags: checkedonce

[Files]
; Service-Binary - alle Dateien aus dem Publish-Output
; Pfad enthält "win-x64", weil dotnet publish mit -r win-x64 einen RID-Subordner anlegt
Source: "..\src\WgAutoswitch.Service\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs

; Tray-Binary
Source: "..\src\WgAutoswitch.Tray\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}\tray"; Flags: ignoreversion recursesubdirs createallsubdirs

; Doku
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\QUICKGUIDE.md"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
; Programmdaten-Verzeichnis mit passenden Rechten anlegen
Name: "{commonappdata}\wg-autoswitch"; Permissions: users-modify

[Icons]
Name: "{group}\{#MyAppName} - Tray starten"; Filename: "{app}\tray\{#MyTrayExe}"
Name: "{group}\Konfiguration bearbeiten"; Filename: "notepad.exe"; Parameters: """{commonappdata}\wg-autoswitch\config.toml"""
Name: "{group}\Log öffnen"; Filename: "notepad.exe"; Parameters: """{commonappdata}\wg-autoswitch\log.txt"""
Name: "{group}\Quickguide"; Filename: "{app}\QUICKGUIDE.md"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

; Autostart per Verknüpfung im Startup-Ordner (wird nur erstellt, wenn Task gewählt)
Name: "{userstartup}\{#MyAppName} Tray"; Filename: "{app}\tray\{#MyTrayExe}"; Tasks: autostart

[InstallDelete]
; Alte Autostart-Verknüpfung aus früheren Versionen entfernen (vor Umbenennung in GuardSwitch)
Type: files; Name: "{userstartup}\WireGuard Auto-Switch Tray.lnk"
Type: files; Name: "{userstartup}\wg-autoswitch Tray.lnk"

[Run]
; Neuen Service registrieren (Stop+Delete eines existierenden Vorgängers
; passiert schon vor der Datei-Extraktion in CurStepChanged(ssInstall),
; sonst sind die .exe-Dateien noch gesperrt).
Filename: "{sys}\sc.exe"; Parameters: "create {#MyServiceName} binPath= ""\""{app}\service\{#MyServiceExe}\"""" DisplayName= ""{#MyAppName}"" start= auto"; Flags: runhidden; StatusMsg: "Registriere Windows-Dienst..."
Filename: "{sys}\sc.exe"; Parameters: "description {#MyServiceName} ""Aktiviert oder deaktiviert WireGuard-Tunnel automatisch je nach Netzwerk."""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#MyServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden

; Service starten
Filename: "{sys}\sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden; StatusMsg: "Starte Dienst..."

; Tray sofort starten und Quickguide anbieten (Postinstall-Optionen)
Filename: "{app}\tray\{#MyTrayExe}"; Description: "Tray-Symbol jetzt starten"; Flags: postinstall nowait skipifsilent
Filename: "notepad.exe"; Parameters: """{app}\QUICKGUIDE.md"""; Description: "Quickguide öffnen"; Flags: postinstall shellexec skipifsilent

[UninstallRun]
; Beim Deinstallieren: Tray killen, Service stoppen und entfernen
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyTrayExe}"; Flags: runhidden; RunOnceId: "KillTray"
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopSvc"
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DelSvc"

[UninstallDelete]
; Logs im ProgramData-Ordner aufräumen, Config behalten falls gewünscht
Type: files; Name: "{commonappdata}\wg-autoswitch\log.txt"

[Code]
// =====================================================================
// Setup-Wizard mit zusätzlichen Seiten für die Konfiguration
// =====================================================================

var
  ConfigPage: TInputQueryWizardPage;
  WireGuardCheckPage: TOutputMsgMemoWizardPage;

procedure InitializeWizard;
begin
  // Hinweisseite: WireGuard muss vorab installiert sein
  WireGuardCheckPage := CreateOutputMsgMemoPage(wpWelcome,
    'Voraussetzungen', 'Bitte vorher prüfen',
    'Damit GuardSwitch funktioniert, muss Folgendes bereits eingerichtet sein:',
    'WICHTIG - bitte vor der Installation prüfen:' + #13#10 +
    '' + #13#10 +
    '1) WireGuard für Windows ist installiert.' + #13#10 +
    '   Download: https://www.wireguard.com/install/' + #13#10 +
    '' + #13#10 +
    '2) Mindestens ein Tunnel ist in WireGuard angelegt und' + #13#10 +
    '   funktioniert manuell. Der Tunnelname (z.B. "home")' + #13#10 +
    '   wird gleich gebraucht.' + #13#10 +
    '' + #13#10 +
    '3) Mindestens EIN Heim-Indikator. Mehr Indikatoren = sicherer:' + #13#10 +
    '' + #13#10 +
    '   a) SSID deines Heim-WLAN  (einfachste Variante)' + #13#10 +
    '' + #13#10 +
    '   b) MAC-Adresse deines Routers  (optional, fälschungssicher;' + #13#10 +
    '      funktioniert auch per LAN-Kabel ohne WLAN)' + #13#10 +
    '      Ermitteln: Eingabeaufforderung -> "arp -a" -> bei der' + #13#10 +
    '      Router-IP die "Physische Adresse" ablesen' + #13#10 +
    '      (Format aa-bb-cc-dd-ee-ff)' + #13#10 +
    '' + #13#10 +
    '   c) IP eines Geräts, das nur zuhause erreichbar ist' + #13#10 +
    '      (optional, z.B. NAS oder Pi)' + #13#10 +
    '' + #13#10 +
    'Hinweis: Ein gefälschtes WLAN mit gleicher SSID kann den Tunnel' + #13#10 +
    'irrtümlich abschalten. Wenn dir das wichtig ist, gib SSID UND' + #13#10 +
    'Router-MAC ein - dann müssen beide übereinstimmen.');

  // Konfigurationsseite mit Eingabefeldern
  ConfigPage := CreateInputQueryPage(wpSelectTasks,
    'Konfiguration', 'Grunddaten für GuardSwitch',
    'Tunnelname ist Pflicht. Für die Heim-Erkennung reicht ein Feld; mehrere ' +
    'erhöhen die Treffsicherheit. Anpassbar später unter Startmenü -> ' +
    'Konfiguration bearbeiten.');

  ConfigPage.Add('Tunnelname (genau wie in WireGuard):', False);
  ConfigPage.Add('Heim-WLAN SSID (empfohlen):', False);
  ConfigPage.Add('Router-MAC, optional - fälschungssicher (aa-bb-cc-dd-ee-ff):', False);
  ConfigPage.Add('IP eines Heim-Geräts, optional (z.B. 192.168.178.5):', False);
  ConfigPage.Add('Port auf diesem Gerät (Standard 22 = SSH):', False);

  ConfigPage.Values[0] := 'home';
  ConfigPage.Values[4] := '22';
end;

// Bei Update (existierende Config) die Konfig-Seiten überspringen.
// WriteInitialConfig würde die Werte sowieso verwerfen (FileExists-Check),
// also den User nicht damit verwirren.
function IsUpgrade(): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonappdata}\wg-autoswitch\config.toml'));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if IsUpgrade() and ((PageID = ConfigPage.ID) or (PageID = WireGuardCheckPage.ID)) then
    Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  TunnelName, Ssid, Mac, ReachableHost: string;
begin
  Result := True;
  if CurPageID = ConfigPage.ID then
  begin
    TunnelName    := Trim(ConfigPage.Values[0]);
    Ssid          := Trim(ConfigPage.Values[1]);
    Mac           := Trim(ConfigPage.Values[2]);
    ReachableHost := Trim(ConfigPage.Values[3]);

    if TunnelName = '' then
    begin
      MsgBox('Bitte einen Tunnelnamen eingeben.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if (Ssid = '') and (Mac = '') and (ReachableHost = '') then
    begin
      MsgBox('Bitte mindestens einen Heim-Indikator ausfüllen' + #13#10 +
             '(SSID, Router-MAC oder Heim-Geräte-IP).' + #13#10 + #13#10 +
             'Sonst kann die App nicht erkennen, ob du zuhause bist.',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if (Mac <> '') and (Length(Mac) <> 17) then
    begin
      if MsgBox('Die MAC-Adresse sieht ungewöhnlich aus.' + #13#10 +
                'Erwartetes Format: aa-bb-cc-dd-ee-ff (17 Zeichen).' + #13#10 + #13#10 +
                'Trotzdem fortfahren?', mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;
end;

// Schreibt eine simple TOML-Konfiguration aus den Wizard-Eingaben
procedure WriteInitialConfig();
var
  ConfigPath, ConfigContent: string;
  TunnelName, Ssid, Mac, ReachableHost, ReachablePort: string;
  CheckCount: Integer;
begin
  TunnelName    := Trim(ConfigPage.Values[0]);
  Ssid          := Trim(ConfigPage.Values[1]);
  Mac           := Trim(ConfigPage.Values[2]);
  ReachableHost := Trim(ConfigPage.Values[3]);
  ReachablePort := Trim(ConfigPage.Values[4]);

  ConfigPath := ExpandConstant('{commonappdata}\wg-autoswitch\config.toml');

  // Nur überschreiben, wenn noch keine Config da ist (Update-Schutz)
  if FileExists(ConfigPath) then
    Exit;

  ForceDirectories(ExpandConstant('{commonappdata}\wg-autoswitch'));

  // min_checks_required = Anzahl der ausgefüllten Heim-Indikatoren.
  // Damit "alle ausgefüllten Checks müssen zustimmen" ohne dass der User
  // den Wert manuell pflegen muss.
  CheckCount := 0;
  if Ssid <> ''          then CheckCount := CheckCount + 1;
  if Mac <> ''           then CheckCount := CheckCount + 1;
  if ReachableHost <> '' then CheckCount := CheckCount + 1;
  if CheckCount = 0      then CheckCount := 1;

  ConfigContent :=
    '# wg-autoswitch Konfiguration' + #13#10 +
    '# Bei Aenderungen: Tray-Rechtsklick -> "Konfiguration neu laden"' + #13#10 +
    '' + #13#10 +
    '[general]' + #13#10 +
    'enabled = true' + #13#10 +
    'check_interval_seconds = 10' + #13#10 +
    'settle_delay_seconds = 3' + #13#10 +
    'check_retries = 2' + #13#10 +
    'hysteresis_count_home = 3' + #13#10 +
    'hysteresis_count_away = 1' + #13#10 +
    'min_checks_required = 1' + #13#10 +
    '' + #13#10 +
    '[[tunnels]]' + #13#10 +
    'name = "' + TunnelName + '"' + #13#10 +
    '' + #13#10 +
    '[home_detection]' + #13#10;

  if Ssid <> '' then
    ConfigContent := ConfigContent + 'ssid = "' + Ssid + '"' + #13#10;
  if Mac <> '' then
    ConfigContent := ConfigContent + 'gateway_mac = "' + Mac + '"' + #13#10;
  if ReachableHost <> '' then
  begin
    ConfigContent := ConfigContent + 'reachable_host = "' + ReachableHost + '"' + #13#10;
    if ReachablePort = '' then ReachablePort := '22';
    ConfigContent := ConfigContent + 'reachable_port = ' + ReachablePort + #13#10;
  end;

  if not SaveStringToFile(ConfigPath, ConfigContent, False) then
    MsgBox('Konfigurationsdatei konnte nicht geschrieben werden:' + #13#10 + ConfigPath,
           mbError, MB_OK);
end;

// Existierenden Tray + Service stoppen, BEVOR der Datei-Kopier-Schritt
// versucht, die laufenden .exe-Dateien zu überschreiben. Sonst greift
// der Restart-Manager und der Installer bricht mit "Anwendungen konnten
// nicht geschlossen werden" ab.
procedure StopExistingInstall();
var
  ResultCode: Integer;
begin
  // Tray killen (läuft als User, hält WgAutoswitch.Tray.exe offen)
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ' + '{#MyTrayExe}',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);

  // Service stoppen (hält WgAutoswitch.Service.exe offen)
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop ' + '{#MyServiceName}',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // sc stop kehrt zurück, sobald der SCM den Stop-Befehl angenommen hat -
  // der Prozess kann noch ein paar Sekunden brauchen bis er wirklich weg ist
  Sleep(2500);

  // Service-Eintrag entfernen, damit binPath frei wird
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete ' + '{#MyServiceName}',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Reihenfolge ist wichtig: erst altes Setup runterfahren (sonst sind die
    // .exe-Dateien gesperrt), dann ggf. Default-Config rausschreiben.
    StopExistingInstall();
    WriteInitialConfig();
  end;
end;

// Vor der Deinstallation den Tray-Prozess sauber beenden
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ' + '{#MyTrayExe}',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  KeepConfig: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    KeepConfig := MsgBox('Konfigurationsdatei behalten?' + #13#10 + #13#10 +
                         'Bei "Ja" bleibt deine Konfiguration für eine spätere ' +
                         'Neuinstallation erhalten.' + #13#10 +
                         'Bei "Nein" werden alle Daten von wg-autoswitch entfernt.',
                         mbConfirmation, MB_YESNO);
    if KeepConfig = IDNO then
      DelTree(ExpandConstant('{commonappdata}\wg-autoswitch'), True, True, True);
  end;
end;
