using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok.Views;

// ═════════════ 🏠 Главная (раскладка и кнопки — по образцу Atoll) ═════════════
public class HomeTab : TabPage
{
    readonly MainWindow _win;
    readonly TextBlock _song = UI.Text("", 14, true), _artist = UI.Text("", 11.5, false, true);
    readonly Image _cover = new() { Width = 46, Height = 46, Stretch = Stretch.UniformToFill };
    readonly TextBlock _coverNote = UI.Icon("\uE8D6", 18, "FgDim");
    readonly TextBlock _wIcon = UI.Text("", 24), _wTemp = UI.Text("", 19, true), _wDesc = UI.Text("", 11.5, false, true);
    RoundButton _play;
    readonly StackPanel _dock = new() { Orientation = Orientation.Horizontal };
    string _dockKey = "";

    public HomeTab(MainWindow win)
    {
        _win = win;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── 1. три большие кнопки с цветными кружками ──
        var r1 = new UniformGrid { Columns = 3, Rows = 1, Margin = new Thickness(-4, -4, -4, 0) };
        r1.Children.Add(UI.ActionPill("\uE7A8", "Область", "#3FA2FF", "#0A6CFF", ScreenshotWindow.StartRegion));
        r1.Children.Add(UI.ActionPill("\uE740", "Весь экран", "#9A98FF", "#5E5CE6", FullShot));
        r1.Children.Add(UI.ActionPill("\uE8D2", "Текст с экрана", "#FFB340", "#FF8A00", ScreenshotWindow.StartOcr));
        SetRow(r1, 0); Children.Add(r1);

        // ── 2. круглые действия с подписями ──
        var r2 = new UniformGrid { Columns = 7, Rows = 1, Margin = new Thickness(0, 8, 0, 8) };
        r2.Children.Add(UI.RoundAction("\uED14", "QR-код", "#32D74B", ScreenshotWindow.StartQr));
        r2.Children.Add(UI.RoundAction("\uE790", "Пипетка", "#FF6482", ScreenshotWindow.StartPipette));
        r2.Children.Add(UI.RoundAction("\uE75C", "Чистый текст", "#64D2FF", CleanClipboard));
        r2.Children.Add(UI.RoundAction("\uE714", "Запись экрана", "#FF453A", () =>
        {
            _win.CloseIsland();
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }); }
            catch { Hub.Notices.Push("Запись", "⏺", "Откройте «Ножницы» (Win+Shift+S) и выберите запись"); }
        }));
        r2.Children.Add(UI.RoundAction("\uE756", "Команда", "#C7C7CC", CommandWindow.ShowIt));
        r2.Children.Add(UI.RoundAction("\uE890", "Значки стола", "#BF5AF2", () => Native.ToggleDesktopIcons()));
        r2.Children.Add(UI.RoundAction("\uEA8F", "Уведомления", "#FFD60A", NotificationsWindow.ShowIt));
        SetRow(r2, 1); Children.Add(r2);

        // ── 3. мини-плеер и погода ──
        var r3 = new Grid();
        r3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
        r3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cb = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Width = 46, Height = 46, Margin = new Thickness(0, 0, 12, 0) };
        cb.SetResourceReference(Border.BackgroundProperty, "ChipBg");
        cb.Child = new Grid { Children = { _coverNote, _cover } };
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        meta.Children.Add(_song); meta.Children.Add(_artist);
        _play = new RoundButton(Glyphs.Play, 34, 15, false, Hub.Media.PlayPause);
        var ctr = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ctr.Children.Add(new RoundButton(Glyphs.Prev, 32, 14, false, Hub.Media.Prev)); ctr.Children.Add(_play); ctr.Children.Add(new RoundButton(Glyphs.Next, 32, 14, false, Hub.Media.Next));
        var pl = new DockPanel();
        DockPanel.SetDock(cb, Dock.Left); pl.Children.Add(cb);
        DockPanel.SetDock(ctr, Dock.Right); pl.Children.Add(ctr);
        pl.Children.Add(meta);
        var playerTile = UI.Tile(pl, 10, 8); playerTile.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(playerTile, 0); r3.Children.Add(playerTile);

        var wp = new DockPanel();
        _wIcon.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(_wIcon, Dock.Left); wp.Children.Add(_wIcon);
        var wt = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; wt.Children.Add(_wTemp); wt.Children.Add(_wDesc);
        wp.Children.Add(wt);
        var weatherTile = UI.Tile(wp, 14, 8);
        Grid.SetColumn(weatherTile, 1); r3.Children.Add(weatherTile);
        SetRow(r3, 2); Children.Add(r3);

        // ── 4. док приложений одной строкой ──
        var dockScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _dock, Margin = new Thickness(0, 6, 0, 0) };
        SetRow(dockScroll, 3); Children.Add(dockScroll);
    }

    void FullShot()
    {
        _win.CloseIsland();
        Task.Delay(400).ContinueWith(_ => Dispatcher.Invoke(ScreenshotWindow.StartFull));
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

    void BuildDock()
    {
        _dock.Children.Clear();
        foreach (var p in Hub.S.Dock.ToList())
        {
            var path = p;
            var img = new Image { Width = 20, Height = 20, Source = AppDock.Icon(path) };
            var b = new Button { Content = img, Style = (Style)Application.Current.FindResource("IconBtn"), Width = 40, Height = 34, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0),
                                 ToolTip = System.IO.Path.GetFileNameWithoutExtension(path) + "  (правая кнопка — убрать)" };
            if (img.Source == null) b.Content = "▫";
            b.Click += (_, _) => { _win.CloseIsland(); AppDock.Launch(path); };
            b.MouseRightButtonUp += (_, e) => { Hub.S.Dock.Remove(path); Hub.S.Save(); _dockKey = ""; Tick(); e.Handled = true; };
            _dock.Children.Add(b);
        }
        var add = UI.Btn("+", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Программы|*.exe;*.lnk|Все файлы|*.*", Title = "Добавить в док" };
            _win.CloseIsland();
            if (dlg.ShowDialog() == true && !Hub.S.Dock.Contains(dlg.FileName)) { Hub.S.Dock.Add(dlg.FileName); Hub.S.Save(); _dockKey = ""; }
        });
        add.Width = 40; add.Height = 34; add.Padding = new Thickness(0); add.Margin = new Thickness(0);
        _dock.Children.Add(add);
    }

    public override void OnShown() { _dockKey = ""; Tick(); }

    public override void Tick()
    {
        var m = Hub.Media;
        if (m.HasSession)
        {
            _song.Text = string.IsNullOrEmpty(m.Title) ? "Без названия" : m.Title;
            _artist.Text = m.Artist;
            _cover.Source = m.Cover;
        }
        else { _song.Text = "Ничего не играет"; _artist.Text = "Включите музыку в любом плеере"; _cover.Source = null; }
        _play.SetPlaying(m.IsPlaying);

        var w = Hub.Weather.Last;
        if (w != null)
        {
            _wIcon.Text = w.Icon; _wTemp.Text = $"{Math.Round(w.Temp)}°";
            _wDesc.Text = string.IsNullOrEmpty(w.City) ? w.Description : $"{w.Description} · {w.City}";
        }
        else { _wIcon.Text = "☁"; _wTemp.Text = "—"; _wDesc.Text = string.IsNullOrEmpty(Hub.S.City) ? "Город — в настройках" : "Загружаю…"; }

        string dk = string.Join("|", Hub.S.Dock);
        if (dk != _dockKey) { _dockKey = dk; BuildDock(); }
    }
}

// ═════════════ 🎵 Музыка (карточка плеера + карточка слов, как в Atoll) ═════════════
public class MusicTab : TabPage
{
    readonly TextBlock _title = UI.Text("", 17, true), _artist = UI.Text("", 13, false, true), _player = UI.Text("", 11.5, false, true);
    readonly Image _cover = new() { Stretch = Stretch.UniformToFill };
    readonly SeekBar _seek = new(), _vol = new();
    readonly TextBlock _pos = UI.Text("0:00", 11, false, true), _left = UI.Text("-0:00", 11, false, true), _volTxt = UI.Text("", 11.5, false, true);
    readonly StackPanel _lyrics = new();
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    readonly RoundButton _play;
    readonly Button _mute;
    RoundButton _fav;
    static readonly Brush Pink = MakePink();
    static Brush MakePink() { var b = new SolidColorBrush(Color.FromRgb(255, 77, 109)); b.Freeze(); return b; }
    int _lastIdx = -2; string _lyricsKey = "";

    public MusicTab()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.75, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        // обложка и названия
        var cb = new Border { Width = 82, Height = 82, CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = _cover, Margin = new Thickness(0, 0, 14, 0) };
        cb.SetResourceReference(Border.BackgroundProperty, "ChipBg");
        _title.TextWrapping = TextWrapping.Wrap; _title.TextTrimming = TextTrimming.CharacterEllipsis; _title.MaxHeight = 46;
        _player.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        meta.Children.Add(_title); meta.Children.Add(_artist); meta.Children.Add(_player);
        var head = new DockPanel(); DockPanel.SetDock(cb, Dock.Left); head.Children.Add(cb); head.Children.Add(meta);
        left.Children.Add(head);

        // время — полоса — сколько осталось
        var times = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        _pos.Width = 38; _left.Width = 42; _left.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(_pos, Dock.Left); times.Children.Add(_pos);
        DockPanel.SetDock(_left, Dock.Right); times.Children.Add(_left);
        _seek.Margin = new Thickness(6, 0, 6, 0); times.Children.Add(_seek);
        left.Children.Add(times);

        // кнопки: контурные «назад/вперёд» и большая белая «пауза»
        _play = new RoundButton(Glyphs.Play, 50, 20, true, Hub.Media.PlayPause);
        var c = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4) };
        c.Children.Add(new RoundButton(Glyphs.Prev, 40, 18, false, Hub.Media.Prev));
        c.Children.Add(_play);
        c.Children.Add(new RoundButton(Glyphs.Next, 40, 18, false, Hub.Media.Next));
        _fav = new RoundButton(Glyphs.Heart, 34, 15, false, ToggleFav);
        c.Children.Add(_fav);
        left.Children.Add(c);

        // громкость
        _mute = new Button { Content = UI.Icon("\uE767", 14, "FgDim"), Style = (Style)Application.Current.FindResource("BareBtn"), Width = 26, Height = 26, ToolTip = "Без звука" };
        _mute.Click += (_, _) => { Hub.Volume.Muted = !Hub.Volume.Muted; Tick(); };
        _vol.AlwaysThumb = true; _vol.Margin = new Thickness(6, 0, 10, 0);
        _vol.Previewing += v => { Hub.Volume.Level = v; if (Hub.Volume.Muted && v > 0) Hub.Volume.Muted = false; _volTxt.Text = "Общая · " + (int)Math.Round(v * 100) + "%"; };
        _vol.Format = f => (int)Math.Round(f * 100) + "%";
        _volTxt.Width = 92;
        var vr = new DockPanel();
        DockPanel.SetDock(_mute, Dock.Left); vr.Children.Add(_mute);
        DockPanel.SetDock(_volTxt, Dock.Right); vr.Children.Add(_volTxt);
        vr.Children.Add(_vol);
        left.Children.Add(vr);

        var leftTile = UI.Tile(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = left }, 16, 14);
        leftTile.Margin = new Thickness(0, 0, 10, 0);
        SetColumn(leftTile, 0); Children.Add(leftTile);

        _scroll.Content = _lyrics;
        var lyricsTile = UI.Tile(_scroll, 16, 14);
        SetColumn(lyricsTile, 1); Children.Add(lyricsTile);

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
        _left.Text = "-" + TimerService.Fmt(m.Duration - m.LivePosition < TimeSpan.Zero ? TimeSpan.Zero : m.Duration - m.LivePosition);
        _fav.Tint(m.HasSession && Hub.S.Favorites.Contains(Key) ? Pink : (Brush)Application.Current.Resources["Fg"]);

        if (!_vol.Dragging)
        {
            double lv = Hub.Volume.Level; bool mu = Hub.Volume.Muted;
            _vol.Value = mu ? 0 : lv;
            _volTxt.Text = mu ? "Без звука" : "Общая · " + (int)Math.Round(lv * 100) + "%";
            ((TextBlock)_mute.Content).Text = mu || lv <= 0.001 ? "\uE74F" : "\uE767";
        }

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
            {
                var t = new TextBlock { Text = string.IsNullOrWhiteSpace(l.Text) ? "♪" : l.Text, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), Opacity = 0.4 };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                _lyrics.Children.Add(t);
            }
        else if (Hub.Lyrics.Plain.Length > 0)
        {
            var t = new TextBlock { Text = Hub.Lyrics.Plain, FontSize = 13, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            _lyrics.Children.Add(t);
        }
        else
        {
            var t = UI.Text(Hub.Media.HasSession ? "Слов для этого трека не нашлось" : "Включите трек — слова появятся здесь", 12.5, false, true);
            t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 40, 0, 0);
            _lyrics.Children.Add(t);
        }
    }

    void HighlightLine(int idx)
    {
        for (int i = 0; i < _lyrics.Children.Count; i++)
        {
            var t = (TextBlock)_lyrics.Children[i];
            t.Opacity = i == idx ? 1 : 0.4;
            t.FontSize = i == idx ? 17 : 13;
            if (i == idx)
            {
                t.UpdateLayout();
                var pt = t.TranslatePoint(new Point(0, 0), _lyrics);
                _scroll.ScrollToVerticalOffset(Math.Max(0, pt.Y - 60));
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
            else _desc.Text = Hub.Weather.LastError.Length > 0 ? Hub.Weather.LastError : "Город не найден — попробуйте написать иначе";
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

    public override void OnShown()
    {
        if (Hub.Weather.Last == null && !string.IsNullOrEmpty(Hub.S.City))
            _ = Hub.Weather.RefreshAsync().ContinueWith(_ => Dispatcher.BeginInvoke(Show));
        Show();
    }

    static string Compass(double d) => new[] { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" }[(int)Math.Round(d / 45.0) % 8];

    void Show()
    {
        var w = Hub.Weather.Last;
        if (w == null)
        {
            _big.Text = "";
            _desc.Text = Hub.Weather.LastError.Length > 0 ? Hub.Weather.LastError + ". Нажмите Enter, чтобы повторить."
                       : string.IsNullOrEmpty(Hub.S.City) ? "Введите город и нажмите Enter" : "Загружаю погоду…";
            _desc.TextWrapping = TextWrapping.Wrap; _desc.TextTrimming = TextTrimming.None;
            return;
        }
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
