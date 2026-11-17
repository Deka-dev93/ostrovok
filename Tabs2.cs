using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Ostrovok.Services;

namespace Ostrovok.Views;

// ═════════════ 💬 Чат с ИИ ═════════════
// WebView2 не рисуется внутри прозрачного окна (AllowsTransparency), поэтому чат открывается
// отдельным окном под островом. Вход — один раз, ключи API не нужны; сессия сохраняется.
public class ChatWindow : Window
{
    public static readonly (string id, string name, string url)[] Providers =
    {
        ("claude", "Claude", "https://claude.ai/new"),
        ("gemini", "Gemini", "https://gemini.google.com/app"),
        ("deepseek", "DeepSeek", "https://chat.deepseek.com/"),
        ("chatgpt", "ChatGPT", "https://chatgpt.com/"),
        ("perplexity", "Perplexity", "https://www.perplexity.ai/"),
        ("mistral", "Le Chat", "https://chat.mistral.ai/chat"),
        ("qwen", "Qwen", "https://chat.qwen.ai/"),
        ("grok", "Grok", "https://grok.com/"),
        ("copilot", "Copilot", "https://copilot.microsoft.com/"),
    };

    static ChatWindow _inst;
    readonly WebView2 _web = new();
    readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal };

    public static void ShowChat(string id = null)
    {
        _inst ??= new ChatWindow();
        _inst.Show(); _inst.Activate();
        if (id != null) _inst.Navigate(id);
    }

    ChatWindow()
    {
        Title = "Чат с ИИ — Островок"; Width = 480; Height = 640; Topmost = true;
        Left = SystemParameters.PrimaryScreenWidth / 2 - 240; Top = 60;
        SetResourceReference(BackgroundProperty, "Bg");
        var dock = new DockPanel();
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _bar, Margin = new Thickness(6) };
        DockPanel.SetDock(scroll, Dock.Top); dock.Children.Add(scroll);
        dock.Children.Add(_web);
        Content = dock;

        foreach (var p in Providers)
        {
            var id = p.id;
            _bar.Children.Add(UI.Btn(p.name, () => Navigate(id)));
        }
        _bar.Children.Add(UI.Btn("↗ В браузере", () =>
        {
            var url = _web.Source?.ToString() ?? "https://claude.ai";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }));

        Loaded += async (_, _) =>
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ostrovok", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, dir);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    // ссылки открываем в системном браузере, только http/https
                    e.Handled = true;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.ToString()) { UseShellExecute = true });
                };
                Navigate(Hub.S.ChatProvider);
            }
            catch (Exception ex)
            {
                AppSettings.Log("webview2: " + ex.Message);
                MessageBox.Show("Для чата нужен Microsoft Edge WebView2 Runtime.\nhttps://developer.microsoft.com/microsoft-edge/webview2/", "Островок");
            }
        };
        GotKeyboardFocus += (_, _) => MainWindow.ChatBusy = true;
        Deactivated += (_, _) => MainWindow.ChatBusy = false;
        Closing += (e, a) => { a.Cancel = true; Hide(); MainWindow.ChatBusy = false; };   // сессия живёт, пока работает остров
    }

    void Navigate(string id)
    {
        var p = Providers.FirstOrDefault(x => x.id == id);
        if (p.url == null) return;
        Hub.S.ChatProvider = id; Hub.S.Save();
        if (_web.CoreWebView2 != null) _web.CoreWebView2.Navigate(p.url);
    }
}

public class ChatTab : TabPage
{
    public ChatTab()
    {
        var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        p.Children.Add(UI.Text("💬 Чат с ИИ", 18, true));
        var d = UI.Text("Один раз войдите в аккаунт — ключи не нужны.", 12, false, true); d.Margin = new Thickness(0, 4, 0, 10);
        p.Children.Add(d);
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 460 };
        foreach (var pr in ChatWindow.Providers)
        {
            var id = pr.id;
            wrap.Children.Add(UI.Btn(pr.name, () => { ChatWindow.ShowChat(id); App.Island?.CloseIsland(); }, id == Hub.S.ChatProvider));
        }
        p.Children.Add(wrap);
        Children.Add(p);
    }
}

// ═════════════ ⏱ Таймер ═════════════
public class TimerTab : TabPage
{
    readonly TextBlock _big = UI.Text("00:00", 44, true), _mode = UI.Text("", 12, false, true);
    readonly TextBox _min = UI.Input("минуты");
    readonly Ring _ring = new() { Width = 120, Height = 120 };
    readonly Button _pause;

    public TimerTab()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var ringHost = new Grid { Width = 140, Height = 140 };
        ringHost.Children.Add(_ring);
        _big.FontSize = 26; _big.HorizontalAlignment = HorizontalAlignment.Center;
        ringHost.Children.Add(_big);
        SetColumn(ringHost, 0); Children.Add(ringHost);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_mode);
        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        foreach (var m in new[] { 1, 5, 10, 15, 30, 60 })
        {
            int mm = m;
            presets.Children.Add(UI.Btn($"{m} мин", () => Hub.Timer.StartTimer(TimeSpan.FromMinutes(mm))));
        }
        right.Children.Add(presets);
        var custom = new StackPanel { Orientation = Orientation.Horizontal };
        _min.Width = 60; custom.Children.Add(_min);
        custom.Children.Add(UI.Btn("Старт", () =>
        {
            if (double.TryParse(_min.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
                Hub.Timer.StartTimer(TimeSpan.FromMinutes(v));
        }));
        right.Children.Add(custom);
        var modes = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        modes.Children.Add(UI.Btn("🍅 Помодоро", Hub.Timer.StartPomodoro, true));
        modes.Children.Add(UI.Btn("⏱ Секундомер", Hub.Timer.StartStopwatch));
        _pause = UI.Btn("⏸", Hub.Timer.PauseResume);
        modes.Children.Add(_pause); modes.Children.Add(UI.Btn("⏹", Hub.Timer.Stop));
        right.Children.Add(modes);
        SetColumn(right, 1); Children.Add(right);
    }

    public override void OnShown() => Tick();

    public override void Tick()
    {
        var t = Hub.Timer;
        _big.Text = t.Active ? TimerService.Fmt(t.Remaining) : "00:00";
        _ring.Progress = t.Progress; _ring.InvalidateVisual();
        _pause.Content = t.Running || !t.Active ? "⏸" : "▶";
        _mode.Text = t.Mode switch
        {
            TimerMode.Timer => "Таймер", TimerMode.Stopwatch => "Секундомер",
            TimerMode.Pomodoro => t.PomodoroBreak ? "Помодоро · перерыв" : "Помодоро · фокус", _ => "Выберите время"
        };
    }
}

// ═════════════ 📋 Буфер обмена ═════════════
public class ClipboardTab : TabPage
{
    readonly StackPanel _list = new();
    int _count = -1;

    public ClipboardTab()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        tools.Children.Add(UI.Btn("ABC", () => Transform(s => s.ToUpperInvariant())));
        tools.Children.Add(UI.Btn("abc", () => Transform(s => s.ToLowerInvariant())));
        tools.Children.Add(UI.Btn("Раскладка", () => Transform(TextTools.FixLayout)));
        tools.Children.Add(UI.Btn("Транслит", () => Transform(TextTools.Translit)));
        tools.Children.Add(UI.Btn("Очистить текст", () => Transform(s => System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " "))));
        tools.Children.Add(UI.Btn("🗑 История", () => Hub.Clip.Clear()));
        SetRow(tools, 0); Children.Add(tools);

        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        SetRow(sv, 1); Children.Add(sv);
        Hub.Clip.Changed += () => Dispatcher.BeginInvoke(Rebuild);
    }

    static void Transform(Func<string, string> f)
    {
        try { if (Clipboard.ContainsText()) Clipboard.SetText(f(Clipboard.GetText())); } catch { }
    }

    public override void OnShown() => Rebuild();
    public override void Tick() { if (_count != Hub.Clip.Items.Count) Rebuild(); }

    void Rebuild()
    {
        _count = Hub.Clip.Items.Count;
        _list.Children.Clear();
        if (_count == 0) { _list.Children.Add(UI.Text("Скопируйте что-нибудь — оно появится здесь. История хранится только в памяти.", 12, false, true)); return; }
        foreach (var it in Hub.Clip.Items)
        {
            var item = it;
            var row = new DockPanel();
            if (item.Kind == ClipKind.Image)
            {
                var im = new Image { Source = item.Image, Height = 44, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                row.Children.Add(im);
            }
            else
            {
                var t = UI.Text(item.Preview, 12.5); t.TextWrapping = TextWrapping.NoWrap; row.Children.Add(t);
            }
            var b = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand,
                Style = (Style)Application.Current.FindResource("IconBtn"), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 6, 10, 6) };
            b.Click += (_, _) => Hub.Clip.Put(item);
            _list.Children.Add(b);
        }
    }
}

public static class TextTools
{
    const string En = "qwertyuiop[]asdfghjkl;'zxcvbnm,./`QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?~&^$@#";
    const string Ru = "йцукенгшщзхъфывапролджэячсмитьбю.ёЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,Ё?:;\"№";

    /// <summary>Исправляет текст, набранный не в той раскладке (в обе стороны).</summary>
    public static string FixLayout(string s)
    {
        int ru = s.Count(c => Ru.Contains(c)), en = s.Count(c => En.Contains(c));
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (ru >= en) { int i = Ru.IndexOf(c); sb.Append(i >= 0 && i < En.Length ? En[i] : c); }
            else { int i = En.IndexOf(c); sb.Append(i >= 0 && i < Ru.Length ? Ru[i] : c); }
        }
        return sb.ToString();
    }

    static readonly Dictionary<char, string> Tr = new()
    {
        ['а']="a",['б']="b",['в']="v",['г']="g",['д']="d",['е']="e",['ё']="yo",['ж']="zh",['з']="z",['и']="i",['й']="y",['к']="k",
        ['л']="l",['м']="m",['н']="n",['о']="o",['п']="p",['р']="r",['с']="s",['т']="t",['у']="u",['ф']="f",['х']="kh",['ц']="ts",
        ['ч']="ch",['ш']="sh",['щ']="shch",['ъ']="",['ы']="y",['ь']="",['э']="e",['ю']="yu",['я']="ya"
    };

    public static string Translit(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            var l = char.ToLowerInvariant(c);
            if (!Tr.TryGetValue(l, out var r)) { sb.Append(c); continue; }
            sb.Append(char.IsUpper(c) && r.Length > 0 ? char.ToUpperInvariant(r[0]) + r[1..] : r);
        }
        return sb.ToString();
    }
}

// ═════════════ 📝 Заметки ═════════════
public class NotesTab : TabPage
{
    readonly TextBox _in = UI.Input("Enter — сохранить, Shift+Enter — новая строка");
    readonly StackPanel _list = new();

    public NotesTab()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _in.AcceptsReturn = true; _in.TextWrapping = TextWrapping.Wrap; _in.MinHeight = 38; _in.MaxHeight = 80;
        _in.Margin = new Thickness(0, 0, 0, 8);
        _in.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                var t = _in.Text.Trim();
                if (t.Length == 0) return;
                Hub.S.Notes.Insert(0, new NoteItem { Text = t }); Hub.S.Save();
                _in.Clear(); Rebuild();
            }
        };
        SetRow(_in, 0); Children.Add(_in);
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        SetRow(sv, 1); Children.Add(sv);
    }

    public override void OnShown() { Rebuild(); }

    void Rebuild()
    {
        _list.Children.Clear();
        if (Hub.S.Notes.Count == 0) _list.Children.Add(UI.Text("Заметок пока нет.", 12, false, true));
        foreach (var n in Hub.S.Notes.ToList())
        {
            var note = n;
            var d = new DockPanel();
            var del = UI.Btn("✕", () => { Hub.S.Notes.Remove(note); Hub.S.Save(); Rebuild(); });
            DockPanel.SetDock(del, Dock.Right); d.Children.Add(del);
            var col = new StackPanel();
            var body = UI.Text(note.Text, 12.5); body.TextWrapping = TextWrapping.Wrap; body.TextTrimming = TextTrimming.None;
            col.Children.Add(body); col.Children.Add(UI.Text(note.Created.ToString("d MMM, HH:mm"), 10.5, false, true));
            d.Children.Add(col);
            _list.Children.Add(UI.Card(d, 8));
        }
    }
}
