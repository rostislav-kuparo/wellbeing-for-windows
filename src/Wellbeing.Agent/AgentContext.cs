using Microsoft.Win32;
using Wellbeing.Core.Contracts;
using Wellbeing.Win32;

namespace Wellbeing.Agent;

/// <summary>Трей-агент: значок в трее, heartbeat-ы в службу, уведомления и окно с WebUI.</summary>
sealed class AgentContext : ApplicationContext
{
    readonly AgentOptions _options;
    readonly AgentLog _log;
    readonly Control _ui = new();
    readonly NotifyIcon _tray;
    readonly ToolStripMenuItem _todayItem;
    readonly HeartbeatLoop _heartbeat;
    readonly EventWaitHandle _showRequested;
    readonly RegisteredWaitHandle _showRequestedWait;
    MainWindow? _window;
    volatile bool _locked;

    public AgentContext(AgentOptions options, AgentLog log, bool showWindow)
    {
        _options = options;
        _log = log;
        _ = _ui.Handle; // окно-маршалер для вызовов из фоновых потоков

        _todayItem = new ToolStripMenuItem("Сегодня: …") { Enabled = false };
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Открыть Wellbeing", null, (_, _) => ShowWindow());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_todayItem);
        if (options.Dev)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход (dev)", null, (_, _) => ExitThread()));
        }

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            Text = "Wellbeing",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowWindow();
        };
        _tray.BalloonTipClicked += (_, _) => ShowWindow();

        SystemEvents.SessionSwitch += OnSessionSwitch;

        _showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        _showRequestedWait = ThreadPool.RegisterWaitForSingleObject(_showRequested,
            (_, _) => _ui.BeginInvoke(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _heartbeat = new HeartbeatLoop(options, new ForegroundProbe(), () => _locked, OnHeartbeatResponse, log);
        _heartbeat.Start();
        log.Info($"Агент запущен (PID {Environment.ProcessId}, подключение {options.ConnectionFile})");

        if (showWindow) ShowWindow();
    }

    /// <summary>Блокировка экрана и отключение сессии (смена пользователя) — время не идёт.</summary>
    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect => false,
            _ => _locked,
        };
    }

    void OnHeartbeatResponse(HeartbeatResponse response)
    {
        if (_ui.IsDisposed) return;
        _ui.BeginInvoke(() =>
        {
            var text = response.TrayText.Length > 127 ? response.TrayText[..127] : response.TrayText;
            _tray.Text = text;
            _todayItem.Text = text.Replace("Wellbeing — с", "С", StringComparison.Ordinal);

            if (response.Notifications.Count == 0) return;
            var important = response.Notifications.LastOrDefault(n => n.Important) ?? response.Notifications[^1];
            _tray.ShowBalloonTip(10_000, important.Title, important.Message, important.Important ? ToolTipIcon.Warning : ToolTipIcon.Info);
            _log.Info($"Уведомление: {important.Title} — {important.Message}");
        });
    }

    void ShowWindow()
    {
        if (_window is { IsDisposed: false })
        {
            if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
            _window.Activate();
            return;
        }

        _window = new MainWindow(_options, _log);
        _window.FormClosed += (_, _) => _window = null;
        _window.Show();
        _window.Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _showRequestedWait.Unregister(null);
            _showRequested.Dispose();
            _heartbeat.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _window?.Dispose();
            _ui.Dispose();
        }

        base.Dispose(disposing);
    }
}
