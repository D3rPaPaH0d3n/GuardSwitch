using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Service;
using WgAutoswitch.Shared;

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(o => o.ServiceName = "guardswitch");
}

// Geteilter State zwischen Worker und Pipe-Server
builder.Services.AddSingleton<ServiceState>();
builder.Services.AddSingleton<NetworkDetector>();
builder.Services.AddSingleton<ITunnelController>(sp =>
    OperatingSystem.IsWindows()
        ? new WindowsTunnelController(sp.GetRequiredService<ILogger<WindowsTunnelController>>())
        : new LinuxTunnelController(sp.GetRequiredService<ILogger<LinuxTunnelController>>()));

// Config laden (lädt sich bei jedem Reload neu via ServiceState)
builder.Services.AddSingleton(sp => AppConfig.Load(Paths.ConfigFile));

// Logging: Windows Event Log + File-Log unter ProgramData bzw. /var/log
if (OperatingSystem.IsWindows())
{
    AddWindowsEventLog(builder.Logging);
}
builder.Logging.AddProvider(new FileLoggerProvider(Paths.LogFile));

builder.Services.AddHostedService<MainWorker>();
builder.Services.AddHostedService<PipeHostedService>();

var host = builder.Build();
host.Run();

[SupportedOSPlatform("windows")]
static void AddWindowsEventLog(ILoggingBuilder logging)
{
#pragma warning disable CA1416
    logging.AddEventLog(settings =>
    {
        settings.SourceName = "guardswitch";
    });
#pragma warning restore CA1416
}
