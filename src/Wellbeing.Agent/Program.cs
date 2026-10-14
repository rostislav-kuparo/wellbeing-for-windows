using Microsoft.Web.WebView2.Core;
using Wellbeing.Win32;

namespace Wellbeing.Agent;

static class Program
{
    const string MutexName = @"Local\Wellbeing.Agent";
    public const string ShowEventName = @"Local\Wellbeing.Agent.Show";

    /// <summary>
    /// Один агент на сессию. Повторный запуск с <c>--show</c> (ярлык в меню «Пуск») просит уже работающий
    /// экземпляр открыть окно; без аргументов (автозагрузка, перезапуск службой) — просто выходит.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        var show = args.Contains("--show");
        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (show && EventWaitHandle.TryOpenExisting(ShowEventName, out var showEvent))
            {
                WindowChrome.AllowAnyProcessToTakeForeground();
                showEvent.Set();
                showEvent.Dispose();
            }

            return;
        }

        var options = AgentOptions.FromArgs(args);
        var log = new AgentLog(options.LogFile);
        Application.ThreadException += (_, e) => log.Error("Необработанная ошибка UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("Необработанная ошибка", e.ExceptionObject as Exception);

        // В опубликованной single-file сборке WebView2Loader.dll лежит рядом с exe.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll")))
            CoreWebView2Environment.SetLoaderDllFolderPath(AppContext.BaseDirectory);

        ApplicationConfiguration.Initialize();
        using var context = new AgentContext(options, log, show);
        Application.Run(context);
    }
}
