using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ostrovok.Services;

namespace Ostrovok.Views;

/// <summary>Док приложений на «Главной»: быстрые запуски с иконками.</summary>
public static class AppDock
{
    static readonly Dictionary<string, ImageSource> Cache = new();

    public static string Resolve(string p)
    {
        if (File.Exists(p)) return p;
        foreach (var dir in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
                     .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')))
        {
            try { var f = Path.Combine(dir.Trim(), p); if (File.Exists(f)) return f; } catch { }
        }
        return p;
    }

    public static ImageSource Icon(string path)
    {
        if (Cache.TryGetValue(path, out var c)) return c;
        ImageSource img = null;
        try
        {
            using var ic = System.Drawing.Icon.ExtractAssociatedIcon(Resolve(path));
            if (ic != null)
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze(); img = bs;
            }
        }
        catch { }
        Cache[path] = img;
        return img;
    }

    public static void Launch(string p)
    {
        try { Process.Start(new ProcessStartInfo(Resolve(p)) { UseShellExecute = true }); }
        catch (Exception ex) { AppSettings.Log("dock: " + ex.Message); }
    }
}

/// <summary>Командная строка острова: ссылки, программы, простой счёт, поиск.</summary>
public class CommandWindow : Window
{
    static CommandWindow _inst;
    readonly TextBox _in = UI.Input();
    readonly TextBlock _out = UI.Text("", 12.5, false, true);

    public static void ShowIt()
    {
        App.Island?.CloseIsland();
        if (_inst == null) _inst = new CommandWindow();
        _inst.Show(); _inst.Activate(); _inst._in.Focus(); _inst._in.SelectAll();
    }

    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["калькулятор"] = "calc.exe", ["блокнот"] = "notepad.exe", ["диспетчер"] = "taskmgr.exe", ["диспетчер задач"] = "taskmgr.exe",
        ["параметры"] = "ms-settings:", ["настройки windows"] = "ms-settings:", ["проводник"] = "explorer.exe", ["терминал"] = "powershell.exe",
        ["панель управления"] = "control.exe", ["рисование"] = "mspaint.exe", ["cmd"] = "cmd.exe"
    };

    CommandWindow()
    {
        Title = "Команда — Островок"; Width = 480; SizeToContent = SizeToContent.Height; Topmost = true; ShowInTaskbar = false;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Bg");
        var p = new StackPanel { Margin = new Thickness(14) };
        p.Children.Add(UI.Text("Введите ссылку, программу, пример для счёта или запрос для поиска", 11.5, false, true));
        _in.Margin = new Thickness(0, 8, 0, 6); _in.FontSize = 15;
        p.Children.Add(_in);
        _out.TextWrapping = TextWrapping.Wrap; _out.TextTrimming = TextTrimming.None;
        p.Children.Add(_out);
        Content = p;
        _in.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(_in.Text.Trim()); if (e.Key == Key.Escape) Hide(); };
        Deactivated += (_, _) => { if (IsVisible) Hide(); };
    }

    void Run(string t)
    {
        if (t.Length == 0) return;
        try
        {
            if (Aliases.TryGetValue(t, out var exe)) { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); Hide(); return; }
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[\d\s+\-*/().,%]+$") && System.Text.RegularExpressions.Regex.IsMatch(t, @"[+\-*/%]"))
            {
                var expr = t.Replace(',', '.').Replace("%", " % ");
                var v = Convert.ToDouble(new DataTable().Compute(expr, null), System.Globalization.CultureInfo.InvariantCulture);
                var s = v.ToString("0.########", System.Globalization.CultureInfo.CurrentCulture);
                _out.Text = $"= {s}   (скопировано)";
                try { Clipboard.SetText(s); } catch { }
                return;
            }
            if (t.StartsWith("http://") || t.StartsWith("https://") || System.Text.RegularExpressions.Regex.IsMatch(t, @"^[\w-]+(\.[\w-]+)+(/.*)?$"))
            {
                Process.Start(new ProcessStartInfo(t.StartsWith("http") ? t : "https://" + t) { UseShellExecute = true }); Hide(); return;
            }
            if (File.Exists(AppDock.Resolve(t)) || Directory.Exists(t))
            {
                Process.Start(new ProcessStartInfo(AppDock.Resolve(t)) { UseShellExecute = true }); Hide(); return;
            }
            Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(t)) { UseShellExecute = true });
            Hide();
        }
        catch (Exception ex) { _out.Text = "Не получилось: " + ex.Message; }
    }
}

/// <summary>История уведомлений.</summary>
public class NotificationsWindow : Window
{
    static NotificationsWindow _inst;
    public static void ShowIt()
    {
        App.Island?.CloseIsland();
        _inst?.Close();
        _inst = new NotificationsWindow();
        _inst.Show(); _inst.Activate();
    }

    NotificationsWindow()
    {
        Title = "Уведомления — Островок"; Width = 400; Height = 460; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Bg");
        var list = new StackPanel { Margin = new Thickness(14) };
        list.Children.Add(UI.Text("🔔 Уведомления", 16, true));
        if (Hub.Notices.History.Count == 0) { var t = UI.Text("Пока тихо.", 12.5, false, true); t.Margin = new Thickness(0, 10, 0, 0); list.Children.Add(t); }
        foreach (var n in Hub.Notices.History.Take(40))
        {
            var row = new StackPanel();
            row.Children.Add(UI.Text($"{n.Title}  ·  {n.App}", 12.5, true));
            if (!string.IsNullOrWhiteSpace(n.Body)) { var b = UI.Text(n.Body, 12, false, true); b.TextWrapping = TextWrapping.Wrap; b.TextTrimming = TextTrimming.None; row.Children.Add(b); }
            row.Children.Add(UI.Text(Links.Ago(n.Time), 10.5, false, true));
            var card = UI.Card(row, 9); card.Margin = new Thickness(0, 8, 0, 0);
            list.Children.Add(card);
        }
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
    }
}
