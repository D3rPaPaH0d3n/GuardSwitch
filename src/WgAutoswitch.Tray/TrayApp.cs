using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Tray;

public class TrayApp : ApplicationContext
{
    // Sichtbarer App-Name (Tooltip/Benachrichtigungen). Der technische Bezeichner
    // technischer Bezeichner für Dienst/Pipe/Config-Ordner ist "guardswitch".
    private const string AppName = "GuardSwitch";

    // Spenden-Link (Revolut Pocket von @D3rPaPaH0d3n).
    private const string DonateUrl = "https://revolut.me/mkainer/pocket/QAt1Q0Ntsb";

    private readonly NotifyIcon _icon = new();
    private readonly PipeClient _client = new();
    private readonly System.Windows.Forms.Timer _pollTimer;
    private StatusMessage? _last;
    // Hysterese gegen Pipe-Aussetzer: erst nach mehreren Fehlern in Folge auf rot
    private int _consecutiveErrors;
    private const int ErrorThreshold = 3;
    // Icons werden einmal beim Start gerendert und wiederverwendet, damit
    // keine HICON-Handles geleakt werden (Bitmap.GetHicon erzeugt Win32-Handles,
    // die nicht von Icon.Dispose freigegeben werden).
    private readonly Dictionary<IconState, Icon> _icons = new();
    private readonly List<IntPtr> _ownedIconHandles = new();
    private IconState _currentIconState = (IconState)(-1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    // Letzter angeforderter State, damit wir nach einem Rebuild (Theme-/DPI-Wechsel)
    // das richtige Icon wieder setzen können.
    private IconState _lastKnownState = IconState.Unknown;

    private ToolStripMenuItem _miStatus = null!;
    private ToolStripMenuItem _miPause = null!;
    private ToolStripSeparator _sep1 = null!;
    private ToolStripMenuItem _miReloadCfg = null!;
    private ToolStripMenuItem _miOpenLog = null!;
    private ToolStripMenuItem _miOpenCfg = null!;
    private ToolStripMenuItem _miDonate = null!;
    private ToolStripMenuItem _miExit = null!;

    public TrayApp()
    {
        BuildIcons();
        BuildMenu();
        _icon.Text = AppName;
        _icon.Visible = true;
        _icon.DoubleClick += async (_, _) => await RefreshAsync();
        SetIcon(IconState.Unknown);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();

        // Auf Theme-/DPI-Wechsel reagieren und Icons neu rendern.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _ = RefreshAsync();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Window or UserPreferenceCategory.Color)
        {
            RebuildIcons();
        }
    }

    private void BuildIcons()
    {
        bool light = IsLightTaskbar();
        int size = IconPixelSize();
        foreach (IconState s in Enum.GetValues<IconState>())
            _icons[s] = MakeShieldIcon(s, size, light);
    }

    // Baut alle Icons neu (z. B. nach Theme-/DPI-Wechsel) und gibt die alten
    // Handles/Icons sauber frei. Setzt anschließend den aktuellen State neu.
    private void RebuildIcons()
    {
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
        foreach (var handle in _ownedIconHandles) DestroyIcon(handle);
        _ownedIconHandles.Clear();

        BuildIcons();
        _currentIconState = (IconState)(-1); // SetIcon erzwingen
        SetIcon(_lastKnownState);
    }

    // Helle Taskleiste? (SystemUsesLightTheme = 1). Bei Fehler: dunkel annehmen.
    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    // DPI-korrekte Tray-Icon-Größe: 16 px logisch, skaliert, gedeckelt für Schärfe.
    private static int IconPixelSize()
    {
        try
        {
            uint dpi = GetDpiForSystem();
            if (dpi == 0) dpi = 96;
            return Math.Clamp((int)Math.Round(16 * dpi / 96.0), 16, 48);
        }
        catch { return 32; }
    }

    private readonly record struct Palette(Color Fill, Color FillEnd, Color Outline, Color Glyph);

    private static Palette PaletteFor(IconState s, bool light)
    {
        // Gesättigte Fluent-Farben mit leichtem Vertikal-Verlauf, weißer Glyph.
        Color glyph = Color.White;
        Color outline = light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255);
        return s switch
        {
            IconState.Home   => new(Color.FromArgb(0x4C, 0xAF, 0x50), Color.FromArgb(0x43, 0x97, 0x46), outline, glyph),
            IconState.Away   => new(Color.FromArgb(0x0A, 0x84, 0xFF), Color.FromArgb(0x00, 0x6C, 0xD8), outline, glyph),
            IconState.Paused => light
                ? new(Color.FromArgb(0x8A, 0x8A, 0x8A), Color.FromArgb(0x73, 0x73, 0x73), outline, glyph)
                : new(Color.FromArgb(0xA8, 0xA8, 0xA8), Color.FromArgb(0x90, 0x90, 0x90), outline, glyph),
            IconState.Error  => new(Color.FromArgb(0xE8, 0x11, 0x23), Color.FromArgb(0xC5, 0x0E, 0x1F), outline, glyph),
            _                => light
                ? new(Color.FromArgb(0x9A, 0xA0, 0xA6), Color.FromArgb(0x80, 0x86, 0x8B), outline, glyph)
                : new(Color.FromArgb(0xB0, 0xB6, 0xBC), Color.FromArgb(0x98, 0x9E, 0xA4), outline, glyph),
        };
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        _miStatus = new ToolStripMenuItem("Status wird geladen…") { Enabled = false };
        _miPause = new ToolStripMenuItem("Auto-Modus pausieren");
        _miPause.Click += async (_, _) => await TogglePauseAsync();

        _sep1 = new ToolStripSeparator();
        _miReloadCfg = new ToolStripMenuItem("Konfiguration neu laden");
        _miReloadCfg.Click += async (_, _) =>
        {
            var resp = await _client.SendAsync(new ReloadConfigCommand(), CancellationToken.None);
            ShowResult(resp, "Config neu geladen");
        };

        _miOpenLog = new ToolStripMenuItem("Log öffnen");
        _miOpenLog.Click += (_, _) =>
        {
            try { Process.Start("notepad.exe", Paths.LogFile); }
            catch { /* nichts */ }
        };

        _miOpenCfg = new ToolStripMenuItem("Konfiguration öffnen");
        _miOpenCfg.Click += (_, _) =>
        {
            try { Process.Start("notepad.exe", Paths.ConfigFile); }
            catch { /* nichts */ }
        };

        _miDonate = new ToolStripMenuItem("💚 Spenden");
        _miDonate.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(DonateUrl) { UseShellExecute = true }); }
            catch { /* nichts */ }
        };

        _miExit = new ToolStripMenuItem("Tray beenden");
        _miExit.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            _miStatus, _miPause, _sep1,
            _miReloadCfg, _miOpenCfg, _miOpenLog,
            new ToolStripSeparator(),
            _miDonate, _miExit
        });
        _icon.ContextMenuStrip = menu;
    }

    private async Task RefreshAsync()
    {
        var resp = await _client.SendAsync(new GetStatusCommand(), CancellationToken.None);
        if (!resp.Success || resp.Status == null)
        {
            _consecutiveErrors++;
            // Transiente Pipe-Fehler (Server zwischen zwei Verbindungen, kurzer Aussetzer)
            // tolerieren – auch beim ersten Start, damit kein Rot-Aufflackern entsteht.
            if (_consecutiveErrors < ErrorThreshold)
                return;

            _last = null;
            SetIcon(IconState.Error);
            _icon.Text = $"{AppName}: {resp.Error ?? "Service nicht erreichbar"}";
            _miStatus.Text = "Service nicht erreichbar";
            _miPause.Enabled = false;
            UpdateTunnelMenu(null);
            return;
        }

        _consecutiveErrors = 0;
        _last = resp.Status;
        _miPause.Enabled = true;
        _miPause.Text = resp.Status.AutoModeEnabled ? "Auto-Modus pausieren" : "Auto-Modus aktivieren";

        var iconState = !resp.Status.AutoModeEnabled
            ? IconState.Paused
            : resp.Status.AtHome ? IconState.Home : IconState.Away;
        SetIcon(iconState);

        var tooltip = resp.Status.AutoModeEnabled
            ? (resp.Status.AtHome ? "Zuhause - Tunnel aus" : "Unterwegs - Tunnel an")
            : "Pausiert";
        _icon.Text = $"{AppName}: {tooltip}";
        _miStatus.Text = $"{tooltip} ({resp.Status.LastDetectionReason})";

        UpdateTunnelMenu(resp.Status);
    }

    private void UpdateTunnelMenu(StatusMessage? status)
    {
        // Vorhandene Tunnel-Einträge entfernen (zwischen _sep1 und _miReloadCfg eingehängt)
        var menu = _icon.ContextMenuStrip!;
        for (int i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i].Tag is "tunnel") menu.Items.RemoveAt(i);
        }
        if (status == null) return;

        int insertAt = menu.Items.IndexOf(_sep1) + 1;
        foreach (var (name, st) in status.Tunnels)
        {
            var label = $"Tunnel \"{name}\": {st.ServiceState}";
            var item = new ToolStripMenuItem(label) { Tag = "tunnel" };
            var activate = new ToolStripMenuItem("Manuell EIN");
            activate.Click += async (_, _) =>
            {
                var r = await _client.SendAsync(new ManualTunnelCommand(name, true), CancellationToken.None);
                ShowResult(r, $"Tunnel {name} eingeschaltet");
            };
            var deactivate = new ToolStripMenuItem("Manuell AUS");
            deactivate.Click += async (_, _) =>
            {
                var r = await _client.SendAsync(new ManualTunnelCommand(name, false), CancellationToken.None);
                ShowResult(r, $"Tunnel {name} ausgeschaltet");
            };
            item.DropDownItems.Add(activate);
            item.DropDownItems.Add(deactivate);
            menu.Items.Insert(insertAt++, item);
        }
        menu.Items.Insert(insertAt, new ToolStripSeparator { Tag = "tunnel" });
    }

    private async Task TogglePauseAsync()
    {
        if (_last == null) return;
        var newState = !_last.AutoModeEnabled;
        var resp = await _client.SendAsync(new SetAutoModeCommand(newState), CancellationToken.None);
        ShowResult(resp, newState ? "Auto-Modus aktiv" : "Auto-Modus pausiert");
    }

    private void ShowResult(CommandResponse resp, string okText)
    {
        if (resp.Success)
            _icon.ShowBalloonTip(2000, AppName, okText, ToolTipIcon.Info);
        else
            _icon.ShowBalloonTip(3000, $"{AppName} - Fehler", resp.Error ?? "Unbekannt", ToolTipIcon.Warning);
        _ = RefreshAsync();
    }

    private enum IconState { Home, Away, Paused, Error, Unknown }

    private void SetIcon(IconState s)
    {
        _lastKnownState = s;
        // Nur tatsächlich neu zuweisen wenn sich der Zustand ändert. Spart
        // bei jedem 3s-Poll-Tick einen NotifyIcon-Update-Roundtrip.
        if (s == _currentIconState) return;
        _currentIconState = s;
        _icon.Icon = _icons[s];
    }

    private Icon MakeShieldIcon(IconState state, int size, bool light)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            DrawShield(g, size, PaletteFor(state, light), state);
        }
        // Bitmap.GetHicon legt ein Win32-HICON an, das Icon.Dispose NICHT
        // freigibt - wir merken uns den Handle und räumen ihn explizit
        // im Dispose des TrayApp wieder weg.
        IntPtr hIcon = bmp.GetHicon();
        _ownedIconHandles.Add(hIcon);
        return Icon.FromHandle(hIcon);
    }

    // Zeichnet den Fluent-Schild samt zustandsabhängigem Innen-Glyph in eine
    // size×size-Fläche. Statisch & ohne Tray-Abhängigkeit, damit identisch nutzbar.
    private static void DrawShield(Graphics g, int size, Palette pal, IconState state)
    {
        float pad = size * 0.10f;
        var bounds = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);

        using var path = BuildShieldPath(bounds);
        // Vertikaler Verlauf über die Schildhöhe für etwas Fluent-Tiefe.
        using (var fillBrush = new LinearGradientBrush(
                   new PointF(bounds.Left, bounds.Top - 1),
                   new PointF(bounds.Left, bounds.Bottom + 1),
                   pal.Fill, pal.FillEnd))
        {
            g.FillPath(fillBrush, path);
        }
        using (var pen = new Pen(pal.Outline, Math.Max(1f, size / 32f)))
            g.DrawPath(pen, path);

        DrawGlyph(g, bounds, pal.Glyph, state);
    }

    // Heraldischer Schild: gerundete Schulter oben, geschwungene Seiten zur Spitze.
    private static GraphicsPath BuildShieldPath(RectangleF b)
    {
        float w = b.Width, h = b.Height;
        float r = w * 0.16f;                 // Eckradius oben
        float cx = b.Left + w / 2f;
        float topY = b.Top;
        float tipY = b.Bottom;               // untere Spitze
        var p = new GraphicsPath();
        // Obere Kante mit gerundeten Ecken
        p.AddArc(b.Left, topY, 2 * r, 2 * r, 180, 90);            // obere linke Ecke
        p.AddArc(b.Right - 2 * r, topY, 2 * r, 2 * r, 270, 90);   // obere rechte Ecke
        // Rechte Seite geschwungen nach unten zur Spitze
        p.AddBezier(b.Right, topY + r,
                    b.Right, b.Top + h * 0.55f,
                    b.Right - w * 0.18f, b.Top + h * 0.82f,
                    cx, tipY);
        // Linke Seite von der Spitze zurück nach oben
        p.AddBezier(cx, tipY,
                    b.Left + w * 0.18f, b.Top + h * 0.82f,
                    b.Left, b.Top + h * 0.55f,
                    b.Left, topY + r);
        p.CloseFigure();
        return p;
    }

    private static void DrawGlyph(Graphics g, RectangleF b, Color color, IconState state)
    {
        // Glyph im oberen, breiteren Teil des Schilds zentrieren.
        float w = b.Width, h = b.Height;
        var center = new PointF(b.Left + w / 2f, b.Top + h * 0.42f);
        float u = w * 0.30f; // Glyph-Halbgröße
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, Math.Max(2f, w / 11f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        switch (state)
        {
            case IconState.Home: // Häkchen
                g.DrawLines(pen, new[]
                {
                    new PointF(center.X - u, center.Y + u * 0.05f),
                    new PointF(center.X - u * 0.25f, center.Y + u * 0.75f),
                    new PointF(center.X + u, center.Y - u * 0.7f),
                });
                break;

            case IconState.Away: // Schloss (Bügel + Korpus)
            {
                float bodyW = u * 1.5f, bodyH = u * 1.25f;
                var body = new RectangleF(center.X - bodyW / 2f, center.Y - bodyH * 0.1f, bodyW, bodyH);
                float shackleR = bodyW * 0.32f;
                using var shacklePen = new Pen(color, Math.Max(1.6f, w / 14f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(shacklePen, center.X - shackleR, body.Top - shackleR * 1.4f,
                          shackleR * 2, shackleR * 2, 180, 180);
                using (var bodyPath = RoundedRect(body, bodyW * 0.22f))
                    g.FillPath(brush, bodyPath);
                break;
            }

            case IconState.Paused: // zwei Balken
            {
                float barW = u * 0.42f, barH = u * 1.7f, gap = u * 0.42f;
                using var l = RoundedRect(new RectangleF(center.X - gap - barW, center.Y - barH / 2f, barW, barH), barW * 0.4f);
                using var rr = RoundedRect(new RectangleF(center.X + gap, center.Y - barH / 2f, barW, barH), barW * 0.4f);
                g.FillPath(brush, l);
                g.FillPath(brush, rr);
                break;
            }

            case IconState.Error: // Ausrufezeichen
            {
                float barW = u * 0.34f, barH = u * 1.25f;
                using var stem = RoundedRect(new RectangleF(center.X - barW / 2f, center.Y - barH * 0.7f, barW, barH), barW * 0.45f);
                g.FillPath(brush, stem);
                float dot = barW * 1.05f;
                g.FillEllipse(brush, center.X - dot / 2f, center.Y + barH * 0.55f, dot, dot);
                break;
            }

            default: // Unknown → Fragezeichen via Segoe UI
            {
                float fontSize = h * 0.34f;
                using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("?", font, brush, center, sf);
                break;
            }
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _icon.Visible = false;
            _icon.Dispose();
            _pollTimer.Dispose();
            foreach (var icon in _icons.Values) icon.Dispose();
            _icons.Clear();
            foreach (var handle in _ownedIconHandles) DestroyIcon(handle);
            _ownedIconHandles.Clear();
        }
        base.Dispose(disposing);
    }
}
