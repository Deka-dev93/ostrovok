using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok.Views;

// ═════════════ 🏠 Главная ═════════════
public class HomeTab : TabPage
{
    readonly MainWindow _win;
    readonly TextBlock _song = UI.Text("", 14, true), _artist = UI.Text("", 11.5, false, true);
    readonly TextBlock _line1 = UI.Text("", 12.5, true), _line2 = UI.Text("", 11.5, false, true);
    readonly TextBlock _head = UI.Text("", 12, false, true);
    readonly TextBlock _volName = UI.Text("", 10.5, false, true), _volPct = UI.Text("", 11, false, true);
    readonly Image _cover = new() { Width = 70, Height = 70, Stretch = Stretch.UniformToFill };
    readonly SeekBar _seek = new(), _vol = new();
    readonly TextBlock _pos = UI.Text("0:00", 10.5, false, true), _dur = UI.Text("0:00", 10.5, false, true);
    RoundButton _play;
    readonly Button _mute;
    readonly Grid _player = new(), _recent = new();
    readonly StackPanel _recentList = new();
    readonly StackPanel _dock = new() { Orientation = Orientation.Horizontal };
    string _recentKey = "", _dockKey = "";
    int _lastLine = -2;

    public HomeTab(MainWindow win)
    {
        _win = win;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── левая колонка ──
        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        _head.Margin = new Thickness(2, 0, 0, 4);
        DockPanel.SetDock(_head, Dock.Top); left.Children.Add(_head);

        // док приложений (снизу)
        var dockScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _dock, Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(dockScroll, Dock.Bottom); left.Children.Add(dockScroll);

        // громкость
        _mute = UI.Btn("🔊", () => { Hub.Volume.Muted = !Hub.Volume.Muted; Tick(); });
        _mute.Width = 34;
        _vol.Margin = new Thickness(4, 0, 4, 0); _vol.VerticalAlignment = VerticalAlignment.Center;
        _vol.Previewing += v => { Hub.Volume.Level = v; if (Hub.Volume.Muted && v > 0) Hub.Volume.Muted = false; _volPct.Text = (int)Math.Round(v * 100) + "%"; };
        _vol.Format = f => (int)Math.Round(f * 100) + "%";
        _volPct.Width = 34; _volPct.TextAlignment = TextAlignment.Right;
        var vg = new DockPanel();
        DockPanel.SetDock(_mute, Dock.Left); DockPanel.SetDock(_volPct, Dock.Right);
        vg.Children.Add(_mute); vg.Children.Add(_volPct); vg.Children.Add(_vol);
        var vbox = new StackPanel(); vbox.Children.Add(vg); _volName.Margin = new Thickness(38, -4, 0, 0); vbox.Children.Add(_volName);
        var volCard = UI.Card(vbox, 6); volCard.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(volCard, Dock.Bottom); left.Children.Add(volCard);

        // верхний блок: плеер или недавно скопированное
        BuildPlayer();
        var recentHead = UI.Text("НЕДАВНО СКОПИРОВАНО", 10.5, true, true); recentHead.Margin = new Thickness(2, 0, 0, 4);
        var rp = new StackPanel(); rp.Children.Add(recentHead); rp.Children.Add(_recentList);
        _recent.Children.Add(rp);
        var topHost = new Grid(); topHost.Children.Add(_player); topHost.Children.Add(_recent);
        left.Children.Add(topHost);
        SetColumn(left, 0); Children.Add(left);

        // ── правая колонка: сетка действий ──
        var grid = new UniformGrid { Columns = 2, Rows = 5 };
        void Act(string icon, string label, Action a)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = icon, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center });
            var t = UI.Text(label, 10.5, false, true); t.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(t);
            var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("IconBtn"), Margin = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            b.Click += (_, _) => a();
            grid.Children.Add(b);
        }
        Act("📷", "Область", ScreenshotWindow.StartRegion);
        Act("🖥", "Весь экран", () => { _win.CloseIsland(); Task.Delay(400).ContinueWith(_ => Dispatcher.Invoke(ScreenshotWindow.StartFull)); });
        Act("🔤", "Текст с экрана", ScreenshotWindow.StartOcr);
        Act("▦", "QR-код", ScreenshotWindow.StartQr);
        Act("💧", "Пипетка", ScreenshotWindow.StartPipette);
        Act("🧹", "Чистый текст", CleanClipboard);
        Act("⏺", "Запись экрана", () =>
        {
            _win.CloseIsland();
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }); }
            catch { Hub.Notices.Push("Запись", "⏺", "Откройте «Ножницы» (Win+Shift+S) и выберите запись"); }
        });
        Act("⌘", "Команда", CommandWindow.ShowIt);
        Act("🙈", "Значки стола", () => Native.ToggleDesktopIcons());
        Act("🔔", "Уведомления", NotificationsWindow.ShowIt);
        SetColumn(grid, 1); Children.Add(grid);
    }

    void BuildPlayer()
    {
        var cb = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _cover, Width = 70, Height = 70, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        cb.SetResourceReference(Border.BackgroundProperty, "CardHover");
        var meta = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        meta.Children.Add(_song); meta.Children.Add(_artist);
        _line1.Margin = new Thickness(0, 4, 0, 0);
        meta.Children.Add(_line1); meta.Children.Add(_line2);
        var top = new DockPanel();
        DockPanel.SetDock(cb, Dock.Left); top.Children.Add(cb); top.Children.Add(meta);

        _seek.Format = f => TimerService.Fmt(TimeSpan.FromSeconds(f * Hub.Media.Duration.TotalSeconds));
        _seek.Previewing += f => _pos.Text = TimerService.Fmt(TimeSpan.FromSeconds(f * Hub.Media.Duration.TotalSeconds));
        _seek.Committed += f => Hub.Media.SeekTo(TimeSpan.FromSeconds(f * Hub.Media.Duration.TotalSeconds));
        var times = new DockPanel();
        DockPanel.SetDock(_dur, Dock.Right); times.Children.Add(_dur); times.Children.Add(_pos);

        _play = new RoundButton(Glyphs.Play, 36, 16, true, Hub.Media.PlayPause);
        var ctr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        ctr.Children.Add(new RoundButton(Glyphs.Prev, 30, 14, false, Hub.Media.Prev)); ctr.Children.Add(_play); ctr.Children.Add(new RoundButton(Glyphs.Next, 30, 14, false, Hub.Media.Next));

        var col = new StackPanel();
        col.Children.Add(top); col.Children.Add(_seek); col.Children.Add(times); col.Children.Add(ctr);
        _player.Children.Add(UI.Card(col, 8));
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

    void BuildRecent()
    {
        _recentList.Children.Clear();
        var items = Hub.Clip.Items.Take(3).ToList();
        if (items.Count == 0) { _recentList.Children.Add(UI.Text("Пока ничего не копировали.", 12, false, true)); return; }
        foreach (var it in items)
        {
            var g = new DockPanel();
            var tm = UI.Text(it.Time.ToString("HH:mm"), 10.5, false, true); tm.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(tm, Dock.Right); g.Children.Add(tm);
            var tx = UI.Text(it.Preview.Replace("\r", " ").Replace("\n", " "), 12); g.Children.Add(tx);
            var item = it;
            _recentList.Children.Add(Links.Clickable(UI.Card(g, 8), () =>
            {
                try
                {
                    if (item.Kind == ClipKind.Text) Clipboard.SetText(item.Text);
                    else if (item.Kind == ClipKind.Image && item.Image != null) Clipboard.SetImage(item.Image);
                    Hub.Notices.Push("Буфер", "📋", "Скопировано снова");
                }
                catch { }
            }));
        }
    }

    void BuildDock()
    {
        _dock.Children.Clear();
        foreach (var p in Hub.S.Dock.ToList())
        {
            var path = p;
            var img = new Image { Width = 22, Height = 22, Source = AppDock.Icon(path) };
            var b = new Button { Content = img, Style = (Style)Application.Current.FindResource("IconBtn"), Width = 38, Height = 34, Margin = new Thickness(0, 0, 4, 0),
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
        add.Width = 38; add.Height = 34;
        _dock.Children.Add(add);
    }

    public override void OnShown() { _lastLine = -2; _dockKey = ""; _recentKey = ""; Tick(); }

    public override void Tick()
    {
        var m = Hub.Media;
        bool playing = m.HasSession;
        _player.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        _recent.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;

        var w = Hub.Weather.Last;
        _head.Text = DateTime.Now.ToString("HH:mm") + "  ·  " + DateTime.Now.ToString("ddd, d MMM", new System.Globalization.CultureInfo("ru-RU"))
                     + (w == null ? "" : $"  ·  {w.Icon} {Math.Round(w.Temp)}°");

        if (playing)
        {
            _song.Text = m.Title; _artist.Text = m.Artist;
            _cover.Source = m.Cover;
            _play.SetPlaying(m.IsPlaying);
            double dur = m.Duration.TotalSeconds;
            if (!_seek.Dragging)
            {
                _seek.Value = dur > 0 ? Math.Clamp(m.LivePosition.TotalSeconds / dur, 0, 1) : 0;
                _pos.Text = TimerService.Fmt(m.LivePosition);
            }
            _dur.Text = TimerService.Fmt(m.Duration);
            var L = Hub.Lyrics;
            if (L.Synced)
            {
                int i = L.IndexAt(m.LivePosition);
                string a = i >= 0 && i < L.Lines.Count ? L.Lines[i].Text : "";
                string b = i + 1 < L.Lines.Count ? L.Lines[i + 1].Text : "";
                _line1.Text = string.IsNullOrWhiteSpace(a) ? "♪" : a; _line2.Text = b;
            }
            else { _line1.Text = ""; _line2.Text = ""; }
        }
        else
        {
            string k = string.Join("|", Hub.Clip.Items.Take(3).Select(i => i.Time.Ticks + i.Kind.ToString()));
            if (k != _recentKey) { _recentKey = k; BuildRecent(); }
        }

        string dk = string.Join("|", Hub.S.Dock);
        if (dk != _dockKey) { _dockKey = dk; BuildDock(); }

        if (!_vol.Dragging)
        {
            double lv = Hub.Volume.Level; bool mu = Hub.Volume.Muted;
            _vol.Value = mu ? 0 : lv;
            _volPct.Text = mu ? "🔇" : (int)Math.Round(lv * 100) + "%";
            _mute.Content = mu || lv <= 0.001 ? "🔇" : "🔊";
            _volName.Text = Hub.Volume.Name;
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
        var cb = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = _cover, Margin = new Thickness(0, 0, 10, 0) };
        cb.SetResourceReference(Border.BackgroundProperty, "Card");
        _title.FontSize = 14; _title.TextWrapping = TextWrapping.Wrap; _title.TextTrimming = TextTrimming.None; _title.MaxHeight = 38;
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        meta.Children.Add(_title); meta.Children.Add(_artist); meta.Children.Add(_player);
        var head = new DockPanel(); DockPanel.SetDock(cb, Dock.Left); head.Children.Add(cb); head.Children.Add(meta);
        left.Children.Add(head);
        left.Children.Add(_seek);
        var times = new DockPanel(); DockPanel.SetDock(_dur, Dock.Right); times.Children.Add(_dur); times.Children.Add(_pos);
        left.Children.Add(times);
        _play = new RoundButton(Glyphs.Play, 46, 20, true, Hub.Media.PlayPause);
        var c = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        c.Children.Add(new RoundButton(Glyphs.Prev, 36, 16, false, Hub.Media.Prev)); c.Children.Add(_play); c.Children.Add(new RoundButton(Glyphs.Next, 36, 16, false, Hub.Media.Next));
        _fav = new RoundButton(Glyphs.Heart, 30, 14, false, ToggleFav);
        c.Children.Add(_fav);
        left.Children.Add(c);
        var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = left };
        SetColumn(leftScroll, 0); Children.Add(leftScroll);

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
