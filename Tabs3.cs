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
    readonly string _section;

    public static readonly Dictionary<string, string> KindNames = new()
    {
        ["Fox"] = "Лиса", ["Cat"] = "Кот", ["Bear"] = "Мишка", ["Ghost"] = "Призрак", ["Robot"] = "Робот",
        ["Spark"] = "Искорка", ["Slime"] = "Слайм", ["Frog"] = "Лягушка"
    };

    public SettingsTab(MainWindow win, string section = null)
    {
        _win = win; _section = section;
        Build();
    }

    void Rebuild() { Children.Clear(); Build(); }

    void Build()
    {
        var p = new StackPanel();
        bool W(string s) => _section == null || _section == s;
        if (_section != null) { var h = UI.Text(_section, 20, true); h.Margin = new Thickness(0, 0, 0, 10); p.Children.Add(h); }

        if (W("Персонаж"))
        {
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var big = new CharacterControl { Kind = Hub.S.Character, Width = 96, Height = 96, IsHitTestVisible = false };
            var bb = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(10), Child = big, Margin = new Thickness(0, 0, 14, 0) };
            bb.SetResourceReference(Border.BackgroundProperty, "Card");
            top.Children.Add(bb);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(UI.Text(KindNames.TryGetValue(Hub.S.Character, out var nm) ? nm : Hub.S.Character, 16, true));
            info.Children.Add(UI.Text("Живёт в пилюле, танцует под музыку и реагирует на события", 11.5, false, true));
            info.Children.Add(Check("Показывать персонажа в пилюле", () => Hub.S.ShowCharacter, v => { Hub.S.ShowCharacter = v; _win.ApplyCharacter(); }));
            top.Children.Add(info);
            p.Children.Add(top);
            var chars = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            foreach (var k in CharacterControl.Kinds)
            {
                var kind = k;
                var cc = new CharacterControl { Kind = kind, Width = 52, Height = 52, IsHitTestVisible = false };
                var sp = new StackPanel { Width = 74 };
                sp.Children.Add(cc);
                var t = UI.Text(KindNames.TryGetValue(kind, out var n2) ? n2 : kind, 11); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 3, 0, 0);
                sp.Children.Add(t);
                var b = new Button { Content = sp, Padding = new Thickness(4, 8, 4, 6), Margin = new Thickness(0, 0, 8, 8), Style = (Style)Application.Current.FindResource("IconBtn") };
                if (kind == Hub.S.Character) b.SetResourceReference(Control.BackgroundProperty, "Accent");
                b.Click += (_, _) => { Hub.S.Character = kind; Hub.S.Save(); _win.ApplyCharacter(); Rebuild(); };
                chars.Children.Add(b);
            }
            p.Children.Add(chars);
            p.Children.Add(UI.Text("Новогодние персонажи выбираются во вкладке «Праздники».", 11, false, true));
        }

        if (W("Общие"))
        {
            p.Children.Add(UI.Text("Цвет", 12, true, true));
            var accents = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            foreach (var a2 in Theme.Accents)
            {
                var hex = a2;
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
            p.Children.Add(Check("Прятаться в играх и полноэкранном видео", () => Hub.S.HideInFullscreen, v => Hub.S.HideInFullscreen = v));
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
        }

        if (W("Живые события"))
        {
            p.Children.Add(UI.Text("Что показывать в пилюле", 12, true, true));
            p.Children.Add(Check("Строка песни (караоке)", () => Hub.S.ShowLyricsInPill, v => Hub.S.ShowLyricsInPill = v));
            p.Children.Add(Check("Погода рядом со временем", () => Hub.S.ShowWeatherInPill, v => Hub.S.ShowWeatherInPill = v));
            p.Children.Add(Check("Скачивание файлов (имя и скорость)", () => Hub.S.ShowDownloads, v => Hub.S.ShowDownloads = v));
            var h2 = UI.Text("Всплывающие уведомления", 12, true, true); h2.Margin = new Thickness(0, 10, 0, 0); p.Children.Add(h2);
            p.Children.Add(Check("Почта", () => Hub.S.PopMail, v => Hub.S.PopMail = v));
            p.Children.Add(Check("Telegram", () => Hub.S.PopTelegram, v => Hub.S.PopTelegram = v));
            p.Children.Add(Check("Наушники (подключение и заряд)", () => Hub.S.PopHeadphones, v => Hub.S.PopHeadphones = v));
            p.Children.Add(Check("Остальные (таймеры, буфер, система)", () => Hub.S.PopSystem, v => Hub.S.PopSystem = v));
            p.Children.Add(Check("Telegram работает в фоне (выключение — после перезапуска)", () => Hub.S.TelegramPreload, v => { Hub.S.TelegramPreload = v; if (v) TelegramWindow.Preload(); }));
        }

        if (W("Клавиши"))
        {
            foreach (var (k, d) in new[] { ("Ctrl+Alt+I", "спрятать / показать остров"), ("Ctrl+Alt+O", "открыть остров"), ("Ctrl+Alt+S", "скриншот области"), ("Ctrl+Alt+T", "светлая / тёмная тема") })
            {
                var row = new DockPanel();
                var kk = UI.Text(k, 12.5, true); kk.Width = 120; row.Children.Add(kk);
                row.Children.Add(UI.Text(d, 12.5, false, true));
                p.Children.Add(UI.Card(row, 9));
            }
            p.Children.Add(UI.Text("Если сочетание занято другой программой, это записывается в журнал (папка данных → log.txt).", 11, false, true));
        }

        if (W("Погода"))
        {
            p.Children.Add(UI.Text("Город", 12, true, true));
            var city = UI.Input("Город и Enter"); city.Text = Hub.S.City; city.Margin = new Thickness(0, 4, 0, 4); city.Width = 260; city.HorizontalAlignment = HorizontalAlignment.Left;
            var st = UI.Text("", 12, false, true);
            city.KeyDown += async (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                st.Text = "Ищу город…";
                if (await Hub.Weather.SetCityAsync(city.Text)) { await Hub.Weather.RefreshAsync(); st.Text = "Готово: " + Hub.S.City; }
                else st.Text = Hub.Weather.LastError.Length > 0 ? Hub.Weather.LastError : "Город не найден — попробуйте написать иначе";
            };
            p.Children.Add(city); p.Children.Add(st);
            p.Children.Add(Check("Показывать погоду в пилюле", () => Hub.S.ShowWeatherInPill, v => Hub.S.ShowWeatherInPill = v));
        }

        if (W("ИИ"))
        {
            p.Children.Add(UI.Text("Какой чат открывать по умолчанию. Ключи не нужны — один раз войдите в аккаунт.", 12, false, true));
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var pr in ChatWindow.Providers)
            {
                var id = pr.id;
                wrap.Children.Add(UI.Btn(pr.name, () => { Hub.S.ChatProvider = id; Hub.S.Save(); Rebuild(); }, id == Hub.S.ChatProvider));
            }
            p.Children.Add(wrap);
            p.Children.Add(UI.Btn("Открыть чат", () => { ChatWindow.ShowChat(Hub.S.ChatProvider); _win.CloseIsland(); }));
        }

        if (W("Звук"))
        {
            var dev = UI.Text(Hub.Volume.Name, 13, true); p.Children.Add(dev);
            var pct = UI.Text((int)Math.Round(Hub.Volume.Level * 100) + "%", 12, false, true);
            var sk = new SeekBar { Value = Hub.Volume.Level, Margin = new Thickness(0, 10, 0, 4) };
            sk.Previewing += v => { Hub.Volume.Level = v; pct.Text = (int)Math.Round(v * 100) + "%"; };
            sk.Format = f => (int)Math.Round(f * 100) + "%";
            p.Children.Add(sk); p.Children.Add(pct);
            p.Children.Add(Check("Звук выключен (mute)", () => Hub.Volume.Muted, v => Hub.Volume.Muted = v));
            p.Children.Add(UI.Text("Громкость также регулируется на «Главной».", 11, false, true));
        }

        if (W("Буфер обмена"))
        {
            p.Children.Add(Check("Пауза: не записывать новые копирования", () => Hub.Clip.Paused, v => Hub.Clip.Paused = v));
            p.Children.Add(UI.Btn("Очистить историю", () => Hub.Clip.Clear()));
            p.Children.Add(UI.Text("История хранится только в памяти, до 30 записей. Пароли из менеджеров не сохраняются.", 11, false, true));
        }

        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = p });
    }

    internal static CheckBox Check(string text, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { IsChecked = get(), Style = (Style)Application.Current.FindResource("Switch") };
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
