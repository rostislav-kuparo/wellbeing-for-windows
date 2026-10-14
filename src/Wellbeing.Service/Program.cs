using System.Net;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using Serilog.Events;
using Wellbeing.Core.Abstractions;
using Wellbeing.Core.Safety;
using Wellbeing.Service;
using Wellbeing.Service.Agent;
using Wellbeing.Service.Api;
using Wellbeing.Service.Blocking;
using Wellbeing.Service.Data;
using Wellbeing.Service.Tracking;
using Wellbeing.Win32;

var isService = WindowsServiceHelpers.IsWindowsService();
var options = ServiceOptions.FromArgs(args, isService);
Directory.CreateDirectory(options.DataDirectory);

var logConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .WriteTo.File(Path.Combine(options.LogDirectory, "service-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
if (!isService) logConfig = logConfig.WriteTo.Console();
Log.Logger = logConfig.CreateLogger();

try
{
    if (isService)
        FileAcl.RestrictDirectoryToSystemAndAdmins(options.DataDirectory);

    var database = new Database(options.DatabasePath);
    database.Migrate();

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Host.UseWindowsService(o => o.ServiceName = ServiceOptions.ServiceName);
    builder.Services.AddSerilog();
    builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, options.Port));

    var services = builder.Services;
    services.AddSingleton(options);
    services.AddSingleton(database);
    services.AddSingleton<IClock, SystemClock>();

    // WinAPI за интерфейсами Core
    services.AddSingleton<IProcessSource, ProcessSource>();
    services.AddSingleton<IProcessTerminator, ProcessTerminator>();
    services.AddSingleton<IProcessStartWatcher, WmiProcessStartWatcher>();
    services.AddSingleton<ISessionInfo, WtsSessionInfo>();
    services.AddSingleton<IUserProcessLauncher, UserProcessLauncher>();
    services.AddSingleton<IUserMessenger, WtsMessenger>();
    services.AddSingleton<IAppMetadataReader, AppMetadataReader>();
    services.AddSingleton(new ProtectionRules(
        [WindowsPaths.Windows, options.InstallDirectory, WindowsPaths.EdgeWebViewDirectory]));

    services.AddSingleton<UsageRepository>();
    services.AddSingleton<AppRepository>();
    services.AddSingleton<PolicyRepository>();
    services.AddSingleton<StateRepository>();

    services.AddSingleton<AgentRegistry>();
    services.AddSingleton<NotificationHub>();
    services.AddSingleton<AppCatalog>();
    services.AddSingleton<PolicyService>();
    services.AddSingleton<SettingsService>();
    services.AddSingleton<EventBroadcaster>();
    services.AddSingleton<Tracker>();
    services.AddSingleton<ProcessGuard>();
    services.AddSingleton(sp => ApiToken.LoadOrCreate(options, sp.GetRequiredService<ILoggerFactory>().CreateLogger("ApiToken")));

    services.AddHostedService<TrackingWorker>();
    services.AddHostedService(sp => sp.GetRequiredService<ProcessGuard>());
    services.AddHostedService<AgentSupervisor>();

    var app = builder.Build();
    app.Services.GetRequiredService<ApiToken>(); // agent.json должен появиться сразу при старте

    app.UseMiddleware<ApiSecurityMiddleware>();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapWellbeingApi();

    Log.Information("Wellbeing запускается: {Mode}, {Url}, данные в {Data}",
        isService ? "служба" : "консоль", options.BaseUrl, options.DataDirectory);
    app.Run();
    return 0;
}
catch (Exception e)
{
    Log.Fatal(e, "Служба остановлена из-за ошибки");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
