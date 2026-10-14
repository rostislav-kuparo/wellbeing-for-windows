using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Wellbeing.Win32;

namespace Wellbeing.Agent;

/// <summary>
/// Окно с WebUI (WebView2). Токен API передаётся в WebView2 как HttpOnly-cookie — JavaScript страницы его не видит.
/// Через postMessage страница может попросить нативный диалог выбора .exe и сообщить о смене темы.
/// </summary>
sealed class MainWindow : Form
{
    const string TokenCookie = "wb_token";
    static readonly Color DarkBackground = Color.FromArgb(9, 9, 11);
    static readonly Color LightBackground = Color.FromArgb(250, 250, 250);

    readonly AgentOptions _options;
    readonly AgentLog _log;
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = DarkBackground };
    Uri? _baseUri;

    public MainWindow(AgentOptions options, AgentLog log)
    {
        _options = options;
        _log = log;
        Text = "Wellbeing";
        Icon = AppIcon.Load(new Size(32, 32));
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 860);
        MinimumSize = new Size(900, 620);
        BackColor = DarkBackground;
        Controls.Add(_web);
        Load += async (_, _) => await InitializeAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.SetDarkTitleBar(Handle, true);
    }

    async Task InitializeAsync()
    {
        var connection = _options.TryReadConnection();
        if (connection is null)
        {
            ShowProblem("Служба Wellbeing недоступна: нет файла подключения.\nПроверьте, что служба установлена и запущена.");
            return;
        }

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, _options.WebViewDataFolder);
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowProblem("Не найден Microsoft Edge WebView2 Runtime.\nУстановите его с сайта Microsoft и откройте окно снова.");
            return;
        }
        catch (Exception e)
        {
            _log.Error("Не удалось запустить WebView2", e);
            ShowProblem("Не удалось открыть интерфейс: " + e.Message);
            return;
        }

        _baseUri = new Uri(connection.BaseUrl.TrimEnd('/') + "/");
        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = _options.Dev;
        core.Settings.AreDefaultContextMenusEnabled = _options.Dev;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;

        var cookie = core.CookieManager.CreateCookie(TokenCookie, connection.Token, _baseUri.Host, "/");
        cookie.IsHttpOnly = true;
        cookie.SameSite = CoreWebView2CookieSameSiteKind.Strict;
        core.CookieManager.AddOrUpdateCookie(cookie);

        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        _web.Source = _baseUri;
    }

    bool IsOwnUri(string uri) =>
        _baseUri is not null && Uri.TryCreate(uri, UriKind.Absolute, out var target)
        && target.Scheme == _baseUri.Scheme && target.Host == _baseUri.Host && target.Port == _baseUri.Port;

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsOwnUri(e.Uri)) return;
        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var target) && target.Scheme is "http" or "https")
            Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
    }

    void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "pickExe":
                    PickExe(root.TryGetProperty("requestId", out var id) ? id.GetString() : null);
                    break;
                case "theme":
                    var dark = root.TryGetProperty("value", out var value) && value.GetString() == "dark";
                    WindowChrome.SetDarkTitleBar(Handle, dark);
                    BackColor = dark ? DarkBackground : LightBackground;
                    _web.DefaultBackgroundColor = BackColor;
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _log.Error("Некорректное сообщение от страницы", ex);
        }
    }

    void PickExe(string? requestId)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите программу",
            Filter = "Программы (*.exe)|*.exe",
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        var path = dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "pickExeResult", requestId, path }));
    }

    void ShowProblem(string text)
    {
        Controls.Clear();
        Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 12f),
        });
    }
}
