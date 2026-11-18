using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok.Views;

// ═════════════ 🏠 Главная ═════════════
public class HomeTab : TabPage
{
    readonly MainWindow _win;
    readonly TextBlock _song = UI.Text("", 13, true), _artist = UI.Text("", 11.5, false, true);
    readonly TextBlock _weather = UI.Text("", 13), _time = UI.Text("", 22, true), _date = UI.Text("", 11.5, false, true);
    readonly Image _cover = new() { Width = 44, Height = 44, Stretch = Stretch.UniformToFill };
    readonly StackPanel _notices = new();
    readonly RoundButton _play;

    public HomeTab(MainWindow win)
    {
        _win = win;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(UI.Btn("📷 Область", ScreenshotWindow.StartRegion));
        actions.Children.Add(UI.Btn("🖥 Весь экран", () => { _win.CloseIsland(); Task.Delay(400).ContinueWith(_ => Dispatcher.Invoke(ScreenshotWindow.StartFull)); }));
        actions.Children.Add(UI.Btn("🔤 Текст с экрана", ScreenshotWindow.StartOcr));
        actions.Children.Add(UI.Btn("🧹 Чистый текст", CleanClipboard));
        actions.Children.Add(UI.Btn("🙈 Значки стола", () => Native.ToggleDesktopIcons()));
        actions.Children.Add(UI.Btn("📂 Папка скриншотов", () =>
        {
            var d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Ostrovok");
            System.IO.Directory.CreateDirectory(d);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d) { UseShellExecute = true });
        }));
        SetRow(actions, 0); Children.Add(actions);

        // мини-плеер + часы/погода
        var left = new StackPanel();
        var player = new DockPanel();
        var coverBorder = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _cover, Margin = new Thickness(0, 0, 10, 0), Width = 44, Height = 44 };
        coverBorder.SetResourceReference(Border.BackgroundProperty, "Card");
        DockPanel.SetDock(coverBorder, Dock.Left);
        var ctr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _play = new RoundButton(Glyphs.Play, 36, 16, true, Hub.Media.PlayPause);
        ctr.Children.Add(new RoundButton(Glyphs.Prev, 30, 14, false, Hub.Media.Prev)); ctr.Children.Add(_play); ctr.Children.Add(new RoundButton(Glyphs.Next, 30, 14, false, Hub.Media.Next));
        var meta = new StackPanel(); meta.Children.Add(_song); meta.Children.Add(_artist);
        var top = new DockPanel(); DockPanel.SetDock(coverBorder, Dock.Left); top.Children.Add(coverBorder); top.Children.Add(meta);
        left.Children.Add(top); left.Children.Add(ctr);
        var playerCard = UI.Card(left);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        right.Children.Add(_time); right.Children.Add(_date); right.Children.Add(_weather);
        var clockCard = UI.Card(right);

        var lr = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        lr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        lr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        playerCard.Margin = new Thickness(0, 0, 6, 6); SetColumn(playerCard, 0); SetColumn(clockCard, 1);
        lr.Children.Add(playerCard); lr.Children.Add(clockCard);

        var body = new DockPanel();
        DockPanel.SetDock(lr, Dock.Top); body.Children.Add(lr);
        var nHead = UI.Text("🔔 Уведомления", 12, true, true); nHead.Margin = new Thickness(2, 2, 0, 4);
        DockPanel.SetDock(nHead, Dock.Top); body.Children.Add(nHead);
        body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _notices });
        SetRow(body, 1); Children.Add(body);
    }

    static void CleanClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            var t = Clipboard.GetText();
            t = System.Text.RegularExpressions.Regex.Replace(t.Trim(), @"[ \t]+", " ");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\r?\n\s*(\r?\n\s*)+", "\n\n");
            Clipboard.SetText(t);
            Hub.Notices.Push("Буфер", "🧹", "Текст очищен от лишнего форматирования");
        }
        catch { }
    }

    public override void OnShown() => Tick();

    public override void Tick()
    {
        var m = Hub.Media;
        _song.Text = m.HasSession ? m.Title : "Ничего не играет";
        _artist.Text = m.HasSession ? m.Artist : "Запустите музыку в любом плеере";
        _cover.Source = m.Cover;
        _play.SetPlaying(m.IsPlaying);
        _time.Text = DateTime.Now.ToString("HH:mm");
        _date.Text = DateTime.Now.ToString("dddd, d MMMM", new System.Globalization.CultureInfo("ru-RU"));
        var w = Hub.Weather.Last;
        _weather.Text = w == null ? "Город — в «Погоде»" : $"{w.Icon} {Math.Round(w.Temp)}° · {w.Description}";

        if (_notices.Children.Count != Hub.Notices.History.Count)
        {
            _notices.Children.Clear();
            if (Hub.Notices.History.Count == 0) _notices.Children.Add(UI.Text("Пока тихо.", 12, false, true));
            foreach (var n in Hub.Notices.History.Take(12))
            {
                var row = new StackPanel();
                row.Children.Add(UI.Text($"{n.Title}  ·  {n.App}", 12, true));
                if (!string.IsNullOrWhiteSpace(n.Body)) row.Children.Add(UI.Text(n.Body, 11.5, false, true));
                _notices.Children.Add(UI.Card(row, 7));
            }
        }
    }
}

// ═════════════ 🎵 Музыка ═════════════
public class MusicTab : TabPage
{
    readonly TextBlock _title = UI.Text("", 16, true), _artist = UI.Text("", 12.5, false, true), _player = UI.Text("", 11, false, true);
    readonly Image _cover = new() { Stretch = Stretch.UniformToFill };
    readonly SeekBar _seek = new();
    readonly TextBlock _pos = UI.Text("0:00", 11, false, true), _dur = UI.Text("0:00", 11, false, true);
    readonly StackPanel _lyrics = new();
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    readonly RoundButton _play;
    RoundButton _fav;
    static readonly Brush Pink = MakePink();
    static Brush MakePink() { var b = new SolidColorBrush(Color.FromRgb(255, 77, 109)); b.Freeze(); return b; }
    int _lastIdx = -2; string _lyricsKey = "";

    public MusicTab()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        var cb = new Border { Height = 150, CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = _cover };
        cb.SetResourceReference(Border.BackgroundProperty, "Card");
        left.Children.Add(cb); left.Children.Add(_title); left.Children.Add(_artist); left.Children.Add(_player);
        left.Children.Add(_seek);
        var times = new DockPanel(); DockPanel.SetDock(_dur, Dock.Right); times.Children.Add(_dur); times.Children.Add(_pos);
        left.Children.Add(times);
        _play = new RoundButton(Glyphs.Play, 46, 20, true, Hub.Media.PlayPause);
        var c = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        c.Children.Add(new RoundButton(Glyphs.Prev, 36, 16, false, Hub.Media.Prev)); c.Children.Add(_play); c.Children.Add(new RoundButton(Glyphs.Next, 36, 16, false, Hub.Media.Next));
        _fav = new RoundButton(Glyphs.Heart, 30, 14, false, ToggleFav);
        c.Children.Add(_fav);
        left.Children.Add(c);
        SetColumn(left, 0); Children.Add(left);

        _scroll.Content = _lyrics; SetColumn(_scroll, 1); Children.Add(_scroll);

        _seek.Format = f => TimerService.Fmt(TimeSpan.FromSeconds(f * Hub.Media.Duration.TotalSeconds));
        _seek.Previewing += f => _pos.Text = TimerService.Fmt(TimeSpan.FromSeconds(f * Hub.Media.Duration.TotalSeconds));
        _seek.Committed += f => Hub.Media.SeekTo(TimeSpan.FromSeconds(f * Hub.Media.Duration.TotalSeconds));
    }

    static string Key => Hub.Media.Artist + " — " + Hub.Media.Title;
    static void ToggleFav()
    {
        if (!Hub.Media.HasSession) return;
        var f = Hub.S.Favorites;
        if (!f.Remove(Key)) f.Add(Key);
        Hub.S.Save();
    }

    public override void OnShown() { _lyricsKey = ""; Tick(); }

    public override void Tick()
    {
        var m = Hub.Media;
        _title.Text = m.HasSession ? m.Title : "Ничего не играет";
        _artist.Text = m.Artist; _player.Text = m.Player.Replace(".exe", "");
        _cover.Source = m.Cover;
        _play.SetPlaying(m.IsPlaying);
        double dur = m.Duration.TotalSeconds;
        if (!_seek.Dragging)
        {
            _seek.Value = dur > 0 ? Math.Clamp(m.LivePosition.TotalSeconds / dur, 0, 1) : 0;
            _pos.Text = TimerService.Fmt(m.LivePosition);
        }
        _dur.Text = TimerService.Fmt(m.Duration);
        _fav.Tint(m.HasSession && Hub.S.Favorites.Contains(Key) ? Pink : (Brush)Application.Current.Resources["Fg"]);

        // слова
        string k = Hub.Lyrics.Synced ? "s" + Hub.Lyrics.Lines.Count + Key : "p" + Hub.Lyrics.Plain.Length + Key;
        if (k != _lyricsKey) { _lyricsKey = k; BuildLyrics(); _lastIdx = -2; }
        if (Hub.Lyrics.Synced)
        {
            int i = Hub.Lyrics.IndexAt(m.LivePosition);
            if (i != _lastIdx) { _lastIdx = i; HighlightLine(i); }
        }
    }

    void BuildLyrics()
    {
        _lyrics.Children.Clear();
        if (Hub.Lyrics.Synced)
            foreach (var l in Hub.Lyrics.Lines)
                _lyrics.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(l.Text) ? "♪" : l.Text, FontSize = 17, FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5), Opacity = 0.35, Foreground = (Brush)Application.Current.Resources["Fg"] });
        else if (Hub.Lyrics.Plain.Length > 0)
            _lyrics.Children.Add(new TextBlock { Text = Hub.Lyrics.Plain, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["Fg"] });
        else
            _lyrics.Children.Add(UI.Text(Hub.Media.HasSession ? "Слов для этого трека не нашлось" : "Включите трек — слова появятся здесь", 13, false, true));
    }

    void HighlightLine(int idx)
    {
        for (int i = 0; i < _lyrics.Children.Count; i++)
        {
            var t = (TextBlock)_lyrics.Children[i];
            t.Opacity = i == idx ? 1 : 0.35;
            t.FontSize = i == idx ? 20 : 17;
            if (i == idx)
            {
                t.UpdateLayout();
                var pt = t.TranslatePoint(new Point(0, 0), _lyrics);
                _scroll.ScrollToVerticalOffset(Math.Max(0, pt.Y - 70));
            }
        }
    }
}

// ═════════════ ☀️ Погода ═════════════
public class WeatherTab : TabPage
{
    readonly TextBox _city = UI.Input("Город и Enter");
    readonly TextBlock _big = UI.Text("", 34, true), _desc = UI.Text("", 13, false, true), _info = UI.Text("", 12, false, true);
    readonly TextBlock _sun = UI.Text("", 12, false, true), _wind = UI.Text("", 12, false, true);
    readonly StackPanel _hours = new() { Orientation = Orientation.Horizontal };

    public WeatherTab()
    {
        _city.Text = Hub.S.City; _city.Width = 200; _city.HorizontalAlignment = HorizontalAlignment.Left;
        _city.KeyDown += async (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            _desc.Text = "Ищу город…";
            if (await Hub.Weather.SetCityAsync(_city.Text)) { await Hub.Weather.RefreshAsync(); Show(); }
            else _desc.Text = "Город не найден";
        };
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(_city, 0); Children.Add(_city);

        var main = new StackPanel { Margin = new Thickness(0, 10, 0, 8) };
        main.Children.Add(_big); main.Children.Add(_desc); main.Children.Add(_info); main.Children.Add(_wind); main.Children.Add(_sun);
        SetRow(main, 1); Children.Add(main);

        var sv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _hours };
        SetRow(sv, 2); Children.Add(sv);
    }

    public override void OnShown() { if (Hub.Weather.Last == null && !string.IsNullOrEmpty(Hub.S.City)) _ = Hub.Weather.RefreshAsync().ContinueWith(_ => Dispatcher.Invoke(Show)); Show(); }

    static string Compass(double d) => new[] { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" }[(int)Math.Round(d / 45.0) % 8];

    void Show()
    {
        var w = Hub.Weather.Last;
        if (w == null) { _big.Text = ""; _desc.Text = "Введите город и нажмите Enter"; return; }
        _big.Text = $"{w.Icon} {Math.Round(w.Temp)}°";
        _desc.Text = $"{w.City} · {w.Description}";
        _info.Text = $"Ощущается как {Math.Round(w.Feels)}° · влажность {Math.Round(w.Humidity)}% · {Math.Round(w.Pressure)} мм рт. ст. · УФ {w.Uv:0.#}";
        _wind.Text = $"Ветер {w.WindSpeed:0.#} м/с, {Compass(w.WindDir)} · осадки {w.Precip:0.#} мм";
        _sun.Text = $"🌅 {w.Sunrise}   🌇 {w.Sunset}";
        _hours.Children.Clear();
        foreach (var h in w.Hours)
        {
            var s = new StackPanel { Width = 58, HorizontalAlignment = HorizontalAlignment.Center };
            s.Children.Add(UI.Text(h.Time, 11, false, true));
            s.Children.Add(UI.Text(WeatherService.IconFor(h.Code, true), 18));
            s.Children.Add(UI.Text($"{Math.Round(h.Temp)}°", 13, true));
            s.Children.Add(UI.Text($"{Math.Round(h.Precip)}%", 10.5, false, true));
            _hours.Children.Add(UI.Card(s, 6));
        }
    }
}
