using Avalonia;

namespace WgAutoswitch.LinuxTray;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mtx = new Mutex(true, "GuardSwitch.LinuxTray.SingleInstance", out bool createdNew);
        if (!createdNew) return;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
