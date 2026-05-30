using Tomlyn;
using Tomlyn.Model;

namespace WgAutoswitch.Shared;

public class AppConfig
{
    public GeneralConfig General { get; set; } = new();
    public List<TunnelConfig> Tunnels { get; set; } = new();
    public HomeDetectionConfig HomeDetection { get; set; } = new();

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = CreateDefault();
            defaultConfig.Save(path);
            return defaultConfig;
        }

        var toml = File.ReadAllText(path);
        var model = Toml.ToModel(toml);
        var cfg = FromModel(model);

        // Sanfte Migration: stammt die Datei aus einer Version vor den neuen
        // Schlüsseln, einmalig auf das aktuelle Schema normalisieren und neu
        // schreiben. Bestehende Werte (Tunnel, Heim-Indikatoren, alte Hysterese)
        // bleiben erhalten; fehlende Felder bekommen Defaults.
        if (NeedsMigration(model))
        {
            cfg.NormalizeAfterMigration();
            try { cfg.Save(path); } catch { /* nicht fatal: Defaults greifen zur Laufzeit */ }
        }
        return cfg;
    }

    private static bool NeedsMigration(TomlTable model)
        => model.TryGetValue("general", out var gen) && gen is TomlTable gt
           && !gt.TryGetValue("hysteresis_count_home", out _);

    private void NormalizeAfterMigration()
    {
        // min_checks_required auf die Anzahl tatsächlich konfigurierter Heim-
        // Indikatoren deckeln (mind. 1). Alte Installer setzten den Wert auf
        // "alle Checks müssen zustimmen" – mit der neuen Tri-State-Logik kann das
        // sonst dauerhaft "unklar" liefern, wenn ein Check nicht anwendbar ist.
        int configured = 0;
        if (!string.IsNullOrWhiteSpace(HomeDetection.GatewayMac)) configured++;
        if (!string.IsNullOrWhiteSpace(HomeDetection.Ssid)) configured++;
        if (!string.IsNullOrWhiteSpace(HomeDetection.ReachableHost) && HomeDetection.ReachablePort > 0) configured++;
        if (configured < 1) configured = 1;
        General.MinChecksRequired = Math.Clamp(General.MinChecksRequired, 1, configured);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# wg-autoswitch configuration");
        sb.AppendLine("# Edit this file then restart the service.");
        sb.AppendLine();
        sb.AppendLine("[general]");
        sb.AppendLine($"enabled = {General.Enabled.ToString().ToLower()}");
        sb.AppendLine($"check_interval_seconds = {General.CheckIntervalSeconds}");
        sb.AppendLine($"settle_delay_seconds = {General.SettleDelaySeconds}");
        sb.AppendLine($"check_retries = {General.CheckRetries}");
        sb.AppendLine("# Asymmetrische Hysterese: langsam/sicher beim Abschalten (zuhause),");
        sb.AppendLine("# schnell beim Einschalten (unterwegs).");
        sb.AppendLine($"hysteresis_count_home = {General.HysteresisCountHome}");
        sb.AppendLine($"hysteresis_count_away = {General.HysteresisCountAway}");
        sb.AppendLine($"min_checks_required = {General.MinChecksRequired}");
        sb.AppendLine();
        foreach (var tunnel in Tunnels)
        {
            sb.AppendLine("[[tunnels]]");
            sb.AppendLine($"name = \"{tunnel.Name}\"");
            sb.AppendLine();
        }
        sb.AppendLine("[home_detection]");
        if (!string.IsNullOrEmpty(HomeDetection.GatewayMac))
            sb.AppendLine($"gateway_mac = \"{HomeDetection.GatewayMac}\"");
        if (!string.IsNullOrEmpty(HomeDetection.Ssid))
            sb.AppendLine($"ssid = \"{HomeDetection.Ssid}\"");
        if (!string.IsNullOrEmpty(HomeDetection.ReachableHost))
        {
            sb.AppendLine($"reachable_host = \"{HomeDetection.ReachableHost}\"");
            sb.AppendLine($"reachable_port = {HomeDetection.ReachablePort}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static AppConfig FromModel(TomlTable model)
    {
        // Per-Feld tolerant lesen: ein einzelner falsch editierter Wert darf nicht
        // den ganzen Dienst lahmlegen. Fehlt/passt ein Feld nicht, greift der Default.
        var d = CreateDefault();
        var cfg = new AppConfig();
        if (model.TryGetValue("general", out var gen) && gen is TomlTable gt)
        {
            cfg.General.Enabled = GetBool(gt, "enabled", d.General.Enabled);
            cfg.General.CheckIntervalSeconds = GetInt(gt, "check_interval_seconds", d.General.CheckIntervalSeconds);
            cfg.General.SettleDelaySeconds = GetInt(gt, "settle_delay_seconds", d.General.SettleDelaySeconds);
            cfg.General.CheckRetries = GetInt(gt, "check_retries", d.General.CheckRetries);
            // hysteresis_count (alt) als Fallback für beide Richtungen akzeptieren.
            var legacy = GetInt(gt, "hysteresis_count", d.General.HysteresisCountHome);
            cfg.General.HysteresisCountHome = GetInt(gt, "hysteresis_count_home", legacy);
            cfg.General.HysteresisCountAway = GetInt(gt, "hysteresis_count_away", d.General.HysteresisCountAway);
            cfg.General.MinChecksRequired = GetInt(gt, "min_checks_required", d.General.MinChecksRequired);
        }
        if (model.TryGetValue("tunnels", out var tArr) && tArr is TomlTableArray arr)
        {
            foreach (var t in arr)
            {
                if (t.TryGetValue("name", out var n) && n is string name && !string.IsNullOrWhiteSpace(name))
                    cfg.Tunnels.Add(new TunnelConfig { Name = name });
            }
        }
        if (model.TryGetValue("home_detection", out var hd) && hd is TomlTable hdt)
        {
            cfg.HomeDetection.GatewayMac = GetStr(hdt, "gateway_mac", "");
            cfg.HomeDetection.Ssid = GetStr(hdt, "ssid", "");
            cfg.HomeDetection.ReachableHost = GetStr(hdt, "reachable_host", "");
            cfg.HomeDetection.ReachablePort = GetInt(hdt, "reachable_port", 0);
        }
        return cfg;
    }

    private static bool GetBool(TomlTable t, string key, bool fallback)
        => t.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    private static int GetInt(TomlTable t, string key, int fallback)
        => t.TryGetValue(key, out var v) && v is long l ? (int)l : fallback;

    private static string GetStr(TomlTable t, string key, string fallback)
        => t.TryGetValue(key, out var v) && v is string s ? s : fallback;

    private static AppConfig CreateDefault() => new()
    {
        General = new GeneralConfig
        {
            Enabled = true,
            CheckIntervalSeconds = 10,
            SettleDelaySeconds = 3,
            CheckRetries = 2,
            HysteresisCountHome = 3,
            HysteresisCountAway = 1,
            MinChecksRequired = 1
        },
        Tunnels = new List<TunnelConfig>
        {
            new() { Name = "home" }
        },
        HomeDetection = new HomeDetectionConfig
        {
            GatewayMac = "",
            Ssid = "",
            ReachableHost = "",
            ReachablePort = 0
        }
    };
}

public class GeneralConfig
{
    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 10;
    // Wartezeit nach einem Netzwerk-Event, bevor gemessen wird (ARP/Gateway/DHCP
    // sollen erst zur Ruhe kommen). Verhindert Fehlmessungen mitten im Wechsel.
    public int SettleDelaySeconds { get; set; } = 3;
    // Zusätzliche Sample-Versuche pro flackeranfälligem Check (ARP, Reachability).
    public int CheckRetries { get; set; } = 2;
    // Asymmetrische Hysterese: wie viele stabile Messungen nötig sind, um den
    // Tunnel ABzuschalten (zuhause erkannt) bzw. ANzuschalten (unterwegs erkannt).
    public int HysteresisCountHome { get; set; } = 3;
    public int HysteresisCountAway { get; set; } = 1;
    public int MinChecksRequired { get; set; } = 1;
}

public class TunnelConfig
{
    public string Name { get; set; } = "";
}

public class HomeDetectionConfig
{
    public string GatewayMac { get; set; } = "";
    public string Ssid { get; set; } = "";
    public string ReachableHost { get; set; } = "";
    public int ReachablePort { get; set; }
}

public static class Paths
{
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "wg-autoswitch");
    public static string ConfigFile => Path.Combine(ConfigDir, "config.toml");
    public static string LogFile => Path.Combine(ConfigDir, "log.txt");
    public const string PipeName = "wg-autoswitch";
}
