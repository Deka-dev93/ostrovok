using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Ostrovok.Views;

/// <summary>Общая среда WebView2 для чата и Telegram: один профиль, вход сохраняется.</summary>
public static class WebEnv
{
    static Task<CoreWebView2Environment> _env;
    public static Task<CoreWebView2Environment> Get()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ostrovok", "WebView2");
        return _env ??= CoreWebView2Environment.CreateAsync(null, dir);
    }
}

/// <summary>
/// Telegram Web в отдельном окне. Окно живёт в фоне за пределами экрана, получает уведомления от Telegram
/// и пересылает их на остров. Закрытие окна просто убирает его с экрана.
/// </summary>
public class TelegramWindow : Window
{
    static TelegramWindow _inst;
    readonly WebView2 _web = new();
    public static bool Running => _inst != null;

    public static void Preload()
    {
        if (_inst != null) return;
        _inst = new TelegramWindow();
        _inst.ShowActivated = false;
        _inst.Show();
    }

    public static void Open()
    {
        if (_inst == null) { _inst = new TelegramWindow(); _inst.ShowActivated = false; _inst.Show(); }
        _inst.Bring();
    }

    TelegramWindow()
    {
        Title = "Telegram — Островок";
        Width = 430; Height = 660;
        ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000;
        SetResourceReference(BackgroundProperty, "Bg");
        Content = _web;
        SourceInitialized += (_, _) => Native.HideFromAltTab(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) => await InitWeb();
        Closing += (_, a) => { a.Cancel = true; Park(); };
        KeyDown += (_, a) => { if (a.Key == System.Windows.Input.Key.Escape) Park(); };
    }

    void Park() { Left = -20000; Top = -20000; }

    void Bring()
    {
        var wa = SystemParameters.WorkArea;
        Left = Math.Max(0, wa.Right - Width - 20);
        Top = Math.Max(0, wa.Top + 50);
        Activate();
        if (!Hub.S.TelegramPreload) { Hub.S.TelegramPreload = true; Hub.S.Save(); }   // один раз вошли — дальше работает в фоне
    }

    async Task InitWeb()
    {
        try
        {
            var env = await WebEnv.Get();
            await _web.EnsureCoreWebView2Async(env);
            var c = _web.CoreWebView2;
            c.PermissionRequested += (_, e) =>
            {
                if (e.PermissionKind == CoreWebView2PermissionKind.Notifications) e.State = CoreWebView2PermissionState.Allow;
            };
            c.NotificationReceived += (_, e) =>
            {
                e.Handled = true;      // системное окошко не показываем — уведомление уйдёт на остров
                try { e.Notification.ReportShown(); } catch { }
                Hub.Telegram.OnNotice(e.Notification.Title, e.Notification.Body);
            };
            c.DocumentTitleChanged += (_, _) => Hub.Telegram.ParseTitle(c.DocumentTitle);
            c.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.ToString()) { UseShellExecute = true });
            };
            c.Navigate("https://web.telegram.org/k/");
        }
        catch (Exception ex) { AppSettings.Log("telegram webview2: " + ex.Message); }
    }
}
