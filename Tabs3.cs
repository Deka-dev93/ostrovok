using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Ostrovok.Services;

namespace Ostrovok.Views;

// ═════════════ 🖥 Система ═════════════
public class SystemTab : TabPage
{
    readonly TextBlock _cpu = UI.Text("", 13), _ram = UI.Text("", 13), _bat = UI.Text("", 13), _gpu = UI.Text("GPU: —", 13), _hp = UI.Text("", 13);
    readonly System.Windows.Controls.ProgressBar _cpuBar = Bar(), _ramBar = Bar();
    readonly List<PerformanceCounter> _gpu3d = new();
    bool _gpuTried;

    static System.Windows.Controls.ProgressBar Bar() => new()
    {
        Height = 6, Maximum = 100, Margin = new Thickness(0, 3, 0, 8), BorderThickness = new Thickness(0),
        Foreground = (Brush)Application.Current.Resources["Accent"], Background = (Brush)Application.Current.Resources["Card"]
    };

    public SystemTab()
    {
        var p = new StackPanel();
        p.Children.Add(_cpu); p.Children.Add(_cpuBar);
        p.Children.Add(_ram); p.Children.Add(_ramBar);
        p.Children.Add(_gpu); p.Children.Add(_bat); p.Children.Add(_hp);
        var dnd = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        dnd.Children.Add(UI.Btn("🌙 Фокус 30 мин", () => Focus(30)));
        dnd.Children.Add(UI.Btn("1 ч", () => Focus(60)));
        dnd.Children.Add(UI.Btn("2 ч", () => Focus(120)));
        p.Children.Add(dnd);
        var power = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        power.Children.Add(UI.Btn("🔒 Блокировка", () => Process.Start("rundll32.exe", "user32.dll,LockWorkStation")));
        power.Children.Add(UI.Btn("📶 Сеть", () => Open("ms-settings:network")));
        power.Children.Add(UI.Btn("🔵 Bluetooth", () => Open("ms-settings:bluetooth")));
        p.Children.Add(power);
        Children.Add(p);
    }

    static void Open(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    // Windows не даёт обычным программам включать «Фокусировку»: открываем параметры и ставим напоминание на конец.
    static void Focus(int minutes)
    {
        Open("ms-settings:quiethours");
        Hub.Notices.Push("Фокус", "🌙", $"Включите «Фокусировку» в параметрах — напомню через {minutes} мин");
        Hub.S.Reminders.Add(new ReminderItem { When = DateTime.Now.AddMinutes(minutes), Text = "Время фокуса вышло" });
        Hub.S.Save();
    }

    public override void OnShown() { Native.CpuPercent(); Tick(); }

    public override void Tick()
    {
        double c = Native.CpuPercent(); var (rp, used, total) = Native.Memory();
        _cpu.Text = $"CPU  {c:0}%"; _cpuBar.Value = c;
        _ram.Text = $"RAM  {rp:0}%  ({used:0.0} / {total:0.0} ГБ)"; _ramBar.Value = rp;
        if (Native.GetSystemPowerStatus(out var s) && s.BatteryLifePercent <= 100)
            _bat.Text = $"🔋 {s.BatteryLifePercent}%" + (s.ACLineStatus == 1 ? " · заряжается" : "");
        else _bat.Text = "🔌 От сети";
        _hp.Text = string.Join("   ", Hub.Headphones.Devices.Select(d => $"🎧 {d.Name}" + (d.Battery >= 0 ? $" · {d.Battery}%" : " · заряд неизвестен")));
        _hp.Visibility = _hp.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (!_gpuTried)
        {
            _gpuTried = true;
            try
            {
                foreach (var inst in new PerformanceCounterCategory("GPU Engine").GetInstanceNames().Where(n => n.EndsWith("engtype_3D")))
                {
                    var pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                    pc.NextValue(); _gpu3d.Add(pc);
                }
            }
            catch { }
        }
        if (_gpu3d.Count == 0) { _gpu.Text = "GPU: недоступно"; return; }
        double sum = 0;
        foreach (var pc in _gpu3d.ToList())
        {
            try { sum += pc.NextValue(); }
            catch { _gpu3d.Remove(pc); pc.Dispose(); }     // процесс-владелец счётчика закрылся
        }
        _gpu.Text = $"GPU  {Math.Min(100, sum):0}%";
    }
}

// ═════════════ ⚙️ Настройки ═════════════
public class SettingsTab : TabPage
{
    readonly MainWindow _win;

    public SettingsTab(MainWindow win)
    {
        _win = win;
        Build();
    }

    void Rebuild() { Children.Clear(); Build(); }

    void Build()
    {
        var p = new StackPanel();

        p.Children.Add(UI.Text("Персонаж", 12, true, true));
        var chars = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var k in CharacterControl.Kinds)
        {
            var kind = k;
            var cc = new CharacterControl { Kind = kind, Width = 44, Height = 44, IsHitTestVisible = false };
            var b = new Button { Content = cc, Padding = new Thickness(4), Margin = new Thickness(0, 0, 6, 0), Style = (Style)Application.Current.FindResource("IconBtn") };
            if (kind == Hub.S.Character) b.SetResourceReference(Control.BackgroundProperty, "Accent");
            b.Click += (_, _) => { Hub.S.Character = kind; Hub.S.Save(); _win.ApplyCharacter(); Rebuild(); };
            chars.Children.Add(b);
        }
        p.Children.Add(chars);

        p.Children.Add(UI.Text("Цвет", 12, true, true));
        var accents = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var a in Theme.Accents)
        {
            var hex = a;
            var sw = new Button
            {
                Width = 24, Height = 24, Margin = new Thickness(0, 0, 6, 0), BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
            };
            sw.Click += (_, _) => { Hub.S.Accent = hex; Hub.S.Save(); Theme.Apply(); };
            accents.Children.Add(sw);
        }
        p.Children.Add(accents);

        p.Children.Add(Check("Стеклянный вид (можно вместе с тёмной темой)", () => Hub.S.Glass, v => { Hub.S.Glass = v; Theme.Apply(); }));
        p.Children.Add(Check("Тёмная тема", () => Hub.S.DarkTheme, v => { Hub.S.DarkTheme = v; Theme.Apply(); }));
        p.Children.Add(Check("Открывать по клику (а не по наведению)", () => Hub.S.OpenOnClick, v => Hub.S.OpenOnClick = v));
        p.Children.Add(Check("Режим «У края» — остров растёт из верхнего края", () => Hub.S.EdgeMode, v => { Hub.S.EdgeMode = v; _win.ApplyEdgeMode(); }));
        p.Children.Add(Check("Строка песни в пилюле (караоке)", () => Hub.S.ShowLyricsInPill, v => Hub.S.ShowLyricsInPill = v));
        p.Children.Add(Check("Прятаться в играх и полноэкранном видео", () => Hub.S.HideInFullscreen, v => Hub.S.HideInFullscreen = v));
        p.Children.Add(Check("Telegram работает в фоне (уведомления; выключение — после перезапуска)", () => Hub.S.TelegramPreload, v => { Hub.S.TelegramPreload = v; if (v) TelegramWindow.Preload(); }));
        p.Children.Add(Check("Запускать вместе с Windows", () => Hub.S.Autostart, v => { Hub.S.Autostart = v; SetAutostart(v); }));

        var ics = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        ics.Children.Add(UI.Text("Календарь: секретная ICS-ссылка (Google / Outlook / Яндекс)", 12, true, true));
        var box = UI.Input(); box.Text = Hub.S.IcsUrl; box.Margin = new Thickness(0, 4, 0, 0);
        box.LostFocus += async (_, _) => { Hub.S.IcsUrl = box.Text.Trim(); Hub.S.Save(); await Hub.Reminders.LoadIcsAsync(); };
        ics.Children.Add(box);
        p.Children.Add(ics);

        var foot = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foot.Children.Add(UI.Btn("📂 Папка данных", () => Process.Start(new ProcessStartInfo(AppSettings.DataDir) { UseShellExecute = true })));
        foot.Children.Add(UI.Btn("Выход", () => { Hub.S.Save(); Application.Current.Shutdown(); }));
        p.Children.Add(foot);
        p.Children.Add(UI.Text("Ctrl+Alt+I — спрятать/показать · Ctrl+Alt+O — открыть · Ctrl+Alt+S — скриншот · Ctrl+Alt+T — тема", 10.5, false, true));

        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = p });
    }

    static CheckBox Check(string text, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { IsChecked = get(), Margin = new Thickness(0, 3, 0, 3), Cursor = Cursors.Hand };
        c.Content = UI.Text(text, 12.5);
        c.Checked += (_, _) => { set(true); Hub.S.Save(); };
        c.Unchecked += (_, _) => { set(false); Hub.S.Save(); };
        return c;
    }

    static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (on) k.SetValue("Ostrovok", "\"" + Environment.ProcessPath + "\"");
            else k.DeleteValue("Ostrovok", false);
        }
        catch (Exception ex) { AppSettings.Log("autostart: " + ex.Message); }
    }
}
