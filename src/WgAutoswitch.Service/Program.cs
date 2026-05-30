using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Service;
using WgAutoswitch.Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "guardswitch");

// Geteilter State zwischen Worker und Pipe-Server
builder.Services.AddSingleton<ServiceState>();
builder.Services.AddSingleton<NetworkDetector>();
builder.Services.AddSingleton<TunnelController>();

// Config laden (lädt sich bei jedem Reload neu via ServiceState)
builder.Services.AddSingleton(sp => AppConfig.Load(Paths.ConfigFile));

// Logging: Windows Event Log + File-Log unter ProgramData
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "guardswitch";
});
builder.Logging.AddProvider(new FileLoggerProvider(Paths.LogFile));

builder.Services.AddHostedService<MainWorker>();
builder.Services.AddHostedService<PipeHostedService>();

var host = builder.Build();
host.Run();
