using System.Windows.Forms;

namespace WgAutoswitch.Tray;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Single-Instance-Schutz: verhindert ein doppeltes Tray-Icon, falls die
        // EXE mehrfach gestartet wird (z. B. alte + neue Autostart-Verknüpfung,
        // Installer-Postinstall + Autostart, manueller Doppelklick). Nutzerlokal
        // (Local\), da pro angemeldetem Nutzer eine Instanz genügt.
        using var mtx = new Mutex(true, @"Local\GuardSwitch.Tray.SingleInstance", out bool createdNew);
        if (!createdNew) return; // läuft bereits → still beenden

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
