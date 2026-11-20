using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Ostrovok.Services;
using Ostrovok.Views;

namespace Ostrovok;

public partial class MainWindow : Window
{
    // ── размеры ──
    const double CompactW = 230, CompactH = 36, ExpW = 600, ExpH = 340;

    readonly Spring _w = new(CompactW), _h = new(CompactH), _y = new(-60), _r = new(18);
    readonly CharacterControl _charC = new(), _charE = new();
    readonly Equalizer _eq = new();
    readonly Ring _ring = new();
    readonly TextBlock _rightText = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    readonly List<(string icon, string name, Func<TabPage> make)> _tabs = new();
    readonly Dictionary<string, TabPage> _pages = new();
    readonly List<Button> _tabBtns = new();
    readonly DispatcherTimer _logic = new() { Interval = TimeSpan.FromMilliseconds(50) };
    DateTime? _inSince, _outSince;
    DateTime _closedAt = DateTime.MinValue;

    bool _open, _userHidden, _dragging, _busyFullscreen;
    int _tabIndex, _activityOffset;
    double _centerX, _topBase;
    DateTime _last = DateTime.Now, _noticeUntil;
    IslandNotice _notice;
    Point _dragStart; double _dragCx, _dragTop;
    HwndSource _src;
    int _lyricIdx = -2;

    public bool IsOpen => _open;

    public MainWindow()
    {
        InitializeComponent();
        AllowDrop = true;
        DragEnter += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) EnterDrop(); };
        PreviewDragOver += (_, _) => _dragSeen = DateTime.Now;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] fs) return;
            int n = 0; foreach (var f in fs) if (System.IO.File.Exists(f) && Hub.Shelf.Add(f) != null) n++;
            if (n > 0) Hub.Notices.Push("Полка", "🗄 На полке", n == 1 ? System.IO.Path.GetFileName(fs[0]) : $"{n} файлов");
        };
        Theme.Apply();
        CompactChar.Content = _charC; ExpChar.Content = _charE;
        Expanded.RenderTransformOrigin = new Point(0.5, 0); Expanded.RenderTransform = _expScale;
        ApplyCharacter();
        CompactRight.Content = _rightText;

        BuildTabs();
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnDragMove;
        MouseLeftButtonUp += OnUp;
        MouseWheel += OnWheel;
        MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        Deactivated += (_, _) => { if (_open && Hub.S.OpenOnClick) SetOpen(false); };
        _logic.Tick += (_, _) => Logic();
    }

    // ───────── запуск ─────────
    async void OnLoaded(object s, RoutedEventArgs e)
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.HideFromAltTab(h);
        _src = HwndSource.FromHwnd(h);
        _src.AddHook(WndProc);
        Hub.Clip.Attach(_src);
        Hub.Downloads.Changed += () => Dispatcher.BeginInvoke(RefreshCompact);
        Hub.Downloads.Start();
        if (Hub.S.ShelfOn) _ = Hub.Shelf.StartAsync();
        RegisterHotkeys(h);

        var wa = SystemParameters.WorkArea;
        _centerX = Hub.S.Left ?? SystemParameters.PrimaryScreenWidth / 2;
        _topBase = Hub.S.EdgeMode ? 0 : (Hub.S.Top ?? 8);
        ClampPosition();
        _y.Value = -CompactH - 6; _y.Target = 0;
        ApplyShape();
        ApplyBounds();

        CompositionTarget.Rendering += OnFrame;
        _logic.Start();

        // события сервисов
        Hub.Media.Changed += () => Dispatcher.BeginInvoke(RefreshCompact);
        Hub.Media.TrackChanged += async () =>
        {
            await Hub.Lyrics.LoadAsync(Hub.Media.Title, Hub.Media.Artist, Hub.Media.Duration.TotalSeconds);
            _lyricIdx = -2;
        };
        Hub.Timer.StateChanged += () => Dispatcher.BeginInvoke(RefreshCompact);
        Hub.Timer.Finished += msg => Dispatcher.BeginInvoke(() => Hub.Notices.Push("Таймер", "⏱", msg));
        Hub.Reminders.Fire += (t, m) => Dispatcher.BeginInvoke(() => Hub.Notices.Push("Напоминание", t, m));
        Hub.Notices.Arrived += n => Dispatcher.BeginInvoke(() => ShowNotice(n));
        Hub.Weather.RefreshAsync().ContinueWith(_ => Dispatcher.BeginInvoke(RefreshCompact));
        var wt = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        wt.Tick += async (_, _) => { await Hub.Weather.RefreshAsync(); RefreshCompact(); };
        wt.Start();
        var it = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        it.Tick += async (_, _) => await Hub.Reminders.LoadIcsAsync();
        it.Start();

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            for (int i = 0; i < _tabs.Count; i++) Page(i);
        }));

        await Hub.Media.InitAsync();
        _ = Hub.Reminders.LoadIcsAsync();
        _ = Hub.Notices.InitSystemAsync();
        Hub.Mail.Changed += () => Dispatcher.BeginInvoke(UpdateBadges);
        Hub.Telegram.Changed += () => Dispatcher.BeginInvoke(UpdateBadges);
        Hub.Mail.Start();
        Holiday.Changed += ApplyHoliday;
        Holiday.Start();
        Hub.Headphones.Start();
        if (Hub.S.TelegramPreload) TelegramWindow.Preload();
        RefreshCompact();
    }

    protected override void OnClosed(EventArgs e)
    {
        Hub.Clip.Detach();
        UnregisterHotkeys();
        base.OnClosed(e);
    }

    // ───────── пружинная анимация ─────────
    void OnFrame(object s, EventArgs e)
    {
        var now = DateTime.Now; double dt = (now - _last).TotalSeconds; _last = now;
        if (_w.Settled && _h.Settled && _y.Settled && _r.Settled)
        {
            if (Expanded.CacheMode != null) Expanded.CacheMode = null;   // анимация закончилась — кэш не нужен, текст чёткий
            if (!Pill.UseLayoutRounding) Pill.UseLayoutRounding = true;
            return;
        }
        // на время анимации прозрачность большого дерева считаем один раз в картинку, а не каждый кадр
        if (Expanded.CacheMode == null) Expanded.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(this).DpiScaleX);

        _w.Step(dt); _h.Step(dt); _y.Step(dt); _r.Step(dt);
        double w = Math.Max(60, _w.Value), h = Math.Max(24, _h.Value);
        Pill.UseLayoutRounding = false;            // субпиксельные края во время движения — без «ступенек»
        Pill.Width = w; Pill.Height = h;
        if (!_y.Settled) ApplyBounds();
        ApplyShape();

        double t = Math.Clamp((_w.Value - CompactW) / (ExpW - CompactW), 0, 1);
        double eIn = Smooth((t - 0.30) / 0.70), cOut = 1 - Smooth(t / 0.35);
        Expanded.Opacity = eIn;
        _expScale.ScaleX = _expScale.ScaleY = 0.93 + 0.07 * eIn;
        Compact.Opacity = cOut;
        Compact.IsHitTestVisible = t < 0.5;
        Expanded.IsHitTestVisible = t > 0.8;
    }

    // Окно ФИКСИРОВАННОГО размера (с запасом под раскрытый остров): меняется только размер пилюли внутри него.
    // Изменение размера окна с прозрачностью каждый кадр заставляет Windows пересоздавать поверхность — отсюда рывки.
    const double WinW = 640, WinH = 360;

    /// <summary>Двигает окно (не меняя размер) в целых физических пикселях — без дрожания при дробном масштабе.</summary>
    void ApplyBounds(double _w_unused = 0, double _h_unused = 0)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int px = (int)Math.Round((_centerX - WinW / 2) * k), py = (int)Math.Round((_topBase + _y.Value) * k);
        Native.SetWindowPos(hwnd, IntPtr.Zero, px, py, 0, 0, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | 0x1 /*SWP_NOSIZE*/);
    }

    /// <summary>Зона наведения в DIP: считается по ЦЕЛЕВОМУ размеру и с запасом по краям — не зависит от анимации окна.</summary>
    (double l, double t, double r, double b) HotZone()
    {
        double m = _open ? 18 : 6;
        double top = _topBase + _y.Value;
        return (_centerX - _w.Target / 2 - m, top <= 4 ? -2000 : top - m, _centerX + _w.Target / 2 + m, top + _h.Target + m);
    }

    static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    readonly ScaleTransform _expScale = new(1, 1);

    void ApplyShape()
    {
        double r = Math.Clamp(Pill.Height / 2, 10, 28);
        Pill.CornerRadius = Hub.S.EdgeMode ? new CornerRadius(0, 0, r, r) : new CornerRadius(r);
        Sheen.CornerRadius = Pill.CornerRadius;
        Garland.SetRadii(Pill.CornerRadius.TopLeft, Pill.CornerRadius.BottomLeft);
        SnowCap.SetRadii(Pill.CornerRadius.TopLeft, Pill.CornerRadius.BottomLeft);
    }

    void SetOpen(bool open)
    {
        if (open && (_userHidden || _busyFullscreen)) return;
        if (_open == open) return;
        _open = open;
        if (open)
        {
            _w.Target = ExpW; _h.Target = ExpH;
            _w.Stiffness = 190; _w.Damping = 24;      // ширина: быстрее
            _h.Stiffness = 150; _h.Damping = 22;      // высота: чуть позже и мягче — остров «наливается»
            ShowTab(_tabIndex);
            _charE.ShowEmotion(Mood.Love, 1.2);
            Hub.Notices.MarkRead();
            if (Hub.S.OpenOnClick) Activate();
        }
        else
        {
            _closedAt = DateTime.Now;
            var (w, h) = CompactSize();
            _w.Target = w; _h.Target = h;
            _w.Stiffness = 210; _w.Damping = 29;
            _h.Stiffness = 250; _h.Damping = 31;
        }
    }

    (double w, double h) CompactSize()
    {
        var a = CurrentActivity();
        return a switch
        {
            Activity.Idle => (CompactW, CompactH),
            Activity.Music => (Hub.S.ShowLyricsInPill && Hub.Lyrics.Synced ? 330 : 270, CompactH),
            Activity.Notice => (340, 38),
            Activity.Download => (300, CompactH),
            _ => (240, CompactH),
        };
    }

    // ───────── живые активности ─────────
    enum Activity { Idle, Music, Timer, Notice, Download }

    List<Activity> Activities()
    {
        var l = new List<Activity>();
        if (_notice != null && DateTime.Now < _noticeUntil) l.Add(Activity.Notice);
        if (Hub.Downloads.Active) l.Add(Activity.Download);
        if (Hub.Timer.Active) l.Add(Activity.Timer);
        if (Hub.Media.HasSession && Hub.Media.IsPlaying) l.Add(Activity.Music);
        if (l.Count == 0) l.Add(Activity.Idle);
        return l;
    }

    Activity CurrentActivity()
    {
        var l = Activities();
        return l[((_activityOffset % l.Count) + l.Count) % l.Count];
    }

    void OnWheel(object s, MouseWheelEventArgs e)
    {
        if (_open) return;
        _activityOffset += e.Delta > 0 ? -1 : 1;
        RefreshCompact();
    }

    static bool AllowPop(IslandNotice n) => n.App switch
    {
        "Почта" => Hub.S.PopMail,
        "Telegram" => Hub.S.PopTelegram,
        "Наушники" => Hub.S.PopHeadphones,
        _ => Hub.S.PopSystem
    };

    void ShowNotice(IslandNotice n)
    {
        if (!AllowPop(n)) return;
        _notice = n; _noticeUntil = DateTime.Now.AddSeconds(5); _activityOffset = 0;
        _charC.ShowEmotion(Mood.Surprised, 1.2);
        RefreshCompact();
    }

    void RefreshCompact()
    {
        var act = CurrentActivity();
        var accent = (Brush)Application.Current.Resources["Accent"];
        _eq.Active = false;
        switch (act)
        {
            case Activity.Notice:
                CompactText.Text = $"{_notice.Title}  {_notice.Body}".Trim();
                CompactRight.Content = _rightText; _rightText.Text = "🔔";
                break;
            case Activity.Download:
                CompactText.Text = $"{Hub.Downloads.Name}  {Hub.Downloads.SpeedText}";
                CompactRight.Content = _rightText; _rightText.Text = "⬇";
                break;
            case Activity.Timer:
                string label = Hub.Timer.Mode == TimerMode.Pomodoro ? (Hub.Timer.PomodoroBreak ? "Перерыв" : "Фокус")
                             : Hub.Timer.Mode == TimerMode.Stopwatch ? "Секундомер" : "Таймер";
                CompactText.Text = $"{label}  {TimerService.Fmt(Hub.Timer.Remaining)}";
                _ring.Progress = Hub.Timer.Progress;
                _ring.InvalidateVisual();
                CompactRight.Content = _ring;
                break;
            case Activity.Music:
                CompactText.Text = LyricOrTitle();
                _eq.Active = Hub.Media.IsPlaying;
                CompactRight.Content = _eq;
                break;
            default:
                var w = Hub.Weather.Last;
                CompactText.Text = DateTime.Now.ToString("HH:mm");
                CompactRight.Content = _rightText;
                _rightText.Text = w == null || !Hub.S.ShowWeatherInPill ? "" : $"{w.Icon} {Math.Round(w.Temp)}°";
                break;
        }
        _rightText.Foreground = (Brush)Application.Current.Resources["Fg"];
        _charC.Dancing = Hub.Media.IsPlaying;
        _charE.Dancing = Hub.Media.IsPlaying;
        Garland.Music = Hub.Media.IsPlaying;
        _charC.Thinking = _charE.Thinking = ChatBusy;
        if (!_open) { var (w2, h2) = CompactSize(); _w.Target = w2; _h.Target = h2; }
    }

    public static bool ChatBusy;

    string LyricOrTitle()
    {
        if (Hub.S.ShowLyricsInPill && Hub.Lyrics.Synced)
        {
            int i = Hub.Lyrics.IndexAt(Hub.Media.LivePosition);
            if (i >= 0 && !string.IsNullOrWhiteSpace(Hub.Lyrics.Lines[i].Text)) return "♪ " + Hub.Lyrics.Lines[i].Text;
        }
        return string.IsNullOrEmpty(Hub.Media.Artist) ? Hub.Media.Title : $"{Hub.Media.Title} — {Hub.Media.Artist}";
    }

    // ───────── главный цикл логики ─────────
    int _tick;
    void Logic()
    {
        _tick++;
        if (!_open && CompactRight.Content == _eq) _eq.InvalidateVisual();
        // курсор в физических пикселях → DIP
        Native.GetCursorPos(out var p);
        double inv = 1.0 / VisualTreeHelper.GetDpi(this).DpiScaleX;
        double cx = p.X * inv, cy = p.Y * inv;

        // персонаж следит за курсором
        double dx = cx - (_centerX - _w.Value / 2 + 30), dy = cy - (_topBase + 20);
        foreach (var c in new[] { _charC, _charE })
        {
            c.LookX = Math.Clamp(dx / 200, -1, 1); c.LookY = Math.Clamp(dy / 200, -1, 1);
            c.Sleeping = !_open && !Hub.Media.IsPlaying && (DateTime.Now - _lastMove).TotalMinutes > 5;
        }
        if (Math.Abs(dx) + Math.Abs(dy) > 3) _lastMove = DateTime.Now;

        // наведение: гистерезис по времени и по зоне — окно не «дребезжит», даже если курсор на границе
        var z = HotZone();
        bool inside = cx >= z.l && cx <= z.r && cy >= z.t && cy <= z.b;
        var now = DateTime.Now;
        if (inside) { _outSince = null; _inSince ??= now; }
        else { _inSince = null; _outSince ??= now; }

        if (!Hub.S.OpenOnClick && !_dragging && !_userHidden && !_busyFullscreen)
        {
            if (!_open && inside && (now - _inSince.Value).TotalMilliseconds > 90 && (now - _closedAt).TotalMilliseconds > 350) SetOpen(true);
            else if (_open && !inside && (now - _outSince.Value).TotalMilliseconds > 300) SetOpen(false);
        }

        // клик мимо открытого острова закрывает его
        if (_open && !inside && !_dragging && (Native.GetAsyncKeyState(0x01) & 0x8000) != 0) SetOpen(false);

        // реже: данные
        if (_tick % 5 == 0)
        {
            if (!_open) RefreshCompact();
            if (_open) Page(_tabIndex)?.Tick();
        }
        if (_tick % 10 == 0)
        {
            bool fs = Hub.S.HideInFullscreen && Native.IsFullscreenAppRunning();
            if (fs != _busyFullscreen) { _busyFullscreen = fs; Holiday.Suspended = fs; Holiday.Apply(); UpdateY(); }
            if (_notice != null && DateTime.Now >= _noticeUntil) { _notice = null; RefreshCompact(); }
        }
    }
    DateTime _lastMove = DateTime.Now;

    void UpdateY()
    {
        bool hide = _userHidden || _busyFullscreen;
        _y.Target = hide ? -(_h.Value + 12) : 0;
        if (hide) SetOpen(false);
    }

    public void ToggleHidden() { _userHidden = !_userHidden; UpdateY(); }
    public void OpenIsland(int tab = -1)
    {
        _userHidden = false; UpdateY();
        if (tab >= 0) _tabIndex = tab;
        SetOpen(true); Activate();
    }
    public void CloseIsland() => SetOpen(false);

    // ───────── перетаскивание ─────────
    void OnDown(object s, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && HasInteractiveParent(d)) return;
        if (Hub.S.OpenOnClick && !_open) { SetOpen(true); return; }
        _dragging = true; _dragStart = CursorDip(); _dragCx = _centerX; _dragTop = _topBase;
        CaptureMouse();
    }

    static bool HasInteractiveParent(DependencyObject d)
    {
        while (d != null)
        {
            if (d is Button or TextBox or Slider or SeekBar or System.Windows.Controls.Primitives.ScrollBar or ComboBox or CheckBox or System.Windows.Controls.Primitives.ToggleButton
                or Microsoft.Web.WebView2.Wpf.WebView2) return true;
            d = d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    Point CursorDip()
    {
        Native.GetCursorPos(out var c);
        double inv = 1.0 / VisualTreeHelper.GetDpi(this).DpiScaleX;
        return new Point(c.X * inv, c.Y * inv);
    }

    void OnDragMove(object s, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = CursorDip();
        _centerX = _dragCx + (p.X - _dragStart.X);
        _topBase = _dragTop + (p.Y - _dragStart.Y);
        ClampPosition();
        ApplyBounds();
    }

    /// <summary>Остров всегда остаётся на экране: нельзя утащить за край или потерять.</summary>
    void ClampPosition()
    {
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double w = SystemParameters.VirtualScreenWidth, h = SystemParameters.VirtualScreenHeight;
        _centerX = Math.Clamp(_centerX, l + 120, l + w - 120);
        _topBase = Hub.S.EdgeMode ? 0 : Math.Clamp(_topBase, t, t + h - 60);
    }

    void OnUp(object s, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false; ReleaseMouseCapture();
        var p = CursorDip();
        // «магнит»: у верхнего края и у центра экрана остров прилипает на своё штатное место
        if (!Hub.S.EdgeMode && _topBase < 24) _topBase = 8;
        if (Math.Abs(_centerX - SystemParameters.PrimaryScreenWidth / 2) < 30) _centerX = SystemParameters.PrimaryScreenWidth / 2;
        ClampPosition(); ApplyBounds();
        Hub.S.Left = _centerX; if (!Hub.S.EdgeMode) Hub.S.Top = _topBase; Hub.S.Save();
    }

    // ───────── правое меню ─────────
    void ShowMenu()
    {
        var m = new ContextMenu();
        void Add(string t, Action a) { var i = new MenuItem { Header = t }; i.Click += (_, _) => a(); m.Items.Add(i); }
        Add("Спрятать (вернуть — Ctrl+Alt+I)", ToggleHidden);
        Add("Вернуть на место (вверх по центру)", () => { _centerX = SystemParameters.PrimaryScreenWidth / 2; _topBase = Hub.S.EdgeMode ? 0 : 8; Hub.S.Left = _centerX; Hub.S.Top = _topBase; Hub.S.Save(); ApplyBounds(); });
        Add(Hub.S.DarkTheme ? "Светлая тема" : "Тёмная тема", ToggleTheme);
        Add(Hub.S.Glass ? "Обычный вид (без стекла)" : "Стеклянный вид", ToggleGlass);
        Add("Настройки", () => SettingsWindow.ShowIt(this));
        m.Items.Add(new Separator());
        Add("Выход", () => { Hub.S.Save(); Application.Current.Shutdown(); });
        m.IsOpen = true;
    }

    public void ToggleGlass()
    {
        Hub.S.Glass = !Hub.S.Glass; Hub.S.Save(); Theme.Apply(); RefreshCompact();
        if (_open) ShowTab(_tabIndex);
    }

    public void ToggleTheme()
    {
        Hub.S.DarkTheme = !Hub.S.DarkTheme; Hub.S.Save(); Theme.Apply(); RefreshCompact();
        if (_open) ShowTab(_tabIndex);
    }

    /// <summary>Применяет праздничные настройки: гирлянда, сугроб, праздничный персонаж.</summary>
    public void ApplyHoliday()
    {
        bool on = Holiday.Active;
        Garland.Mode = Hub.S.HolidayGarland;
        Garland.Visibility = on && Hub.S.HolidayGarland != "Off" ? Visibility.Visible : Visibility.Collapsed;
        SnowCap.Visibility = on && Hub.S.HolidaySnowCap ? Visibility.Visible : Visibility.Collapsed;
        ApplyCharacter();
    }

    public void ApplyCharacter()
    {
        _charC.Kind = _charE.Kind = Holiday.EffectiveCharacter;
        CompactChar.Visibility = Hub.S.ShowCharacter ? Visibility.Visible : Visibility.Collapsed;
        ExpChar.Visibility = Hub.S.ShowCharacter ? Visibility.Visible : Visibility.Collapsed;
    }
    public void ApplyEdgeMode()
    {
        _topBase = Hub.S.EdgeMode ? 0 : (Hub.S.Top ?? 8);
        ApplyShape(); ApplyBounds();
    }

    // ───────── вкладки ─────────
    readonly List<(string icon, string name, Func<TabPage> make)> _all = new();
    static readonly string[] Pinned = { "Вкладки", "Настройки" };
    static readonly string[] InSettings = { "Вкладки", "Настройки", "Праздники" };
    // ───────── зоны сброса файлов ─────────
    Border _dropUi; DateTime _dragSeen; System.Windows.Threading.DispatcherTimer _dragTimer; bool _dropMode;

    void BuildDropUi()
    {
        var uni = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(10) };
        void Zone(string glyph, string title, string sub, Action<string[]> act)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var ic = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            ic.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            var t = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            var s2 = new TextBlock { Text = sub, FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 100, Margin = new Thickness(0, 3, 0, 0) };
            s2.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
            sp.Children.Add(ic); sp.Children.Add(t); sp.Children.Add(s2);
            var rect = new System.Windows.Shapes.Rectangle { RadiusX = 14, RadiusY = 14, StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection { 3, 3 }, Opacity = 0.5 };
            rect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "FgDim");
            var g = new Grid { Margin = new Thickness(4), AllowDrop = true, Background = Brushes.Transparent };
            var fill = new Border { CornerRadius = new CornerRadius(14), Opacity = 0 };
            fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            g.Children.Add(fill); g.Children.Add(rect); g.Children.Add(sp);
            g.DragEnter += (_, _) => { fill.Opacity = 0.28; rect.Opacity = 1; };
            g.DragLeave += (_, _) => { fill.Opacity = 0; rect.Opacity = 0.5; };
            g.Drop += (_, e) =>
            {
                e.Handled = true;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] fs) act(fs);
                ExitDrop();
            };
            uni.Children.Add(g);
        }
        Zone("\uE8B7", "На полку", "положит, пока не понадобится", fs =>
        {
            int n = 0; foreach (var f in fs) if (System.IO.File.Exists(f) && Hub.Shelf.Add(f) != null) n++;
            if (n > 0) Hub.Notices.Push("Полка", "🗄 На полке", n == 1 ? System.IO.Path.GetFileName(fs[0]) : $"{n} файлов");
        });
        Zone("\uE71D", "В лаунчер", "яркий значок в доке на «Главной»", fs =>
        {
            int n = 0;
            foreach (var f in fs) if (!Hub.S.Dock.Contains(f)) { Hub.S.Dock.Add(f); n++; }
            Hub.S.Save();
            Hub.Notices.Push("Лаунчер", "➕ В доке", n == 1 ? System.IO.Path.GetFileName(fs[0]) : $"{n} элементов");
        });
        Zone("\uE8BD", "В чат", "текст файла — в буфер, вставьте в чат", fs =>
        {
            try
            {
                var f = fs[0]; string txt = f;
                var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (System.IO.File.Exists(f) && new System.IO.FileInfo(f).Length < 200_000 && ext is ".txt" or ".md" or ".cs" or ".py" or ".js" or ".json" or ".csv" or ".log" or ".html" or ".xml" or ".css" or ".ts")
                    txt = System.IO.File.ReadAllText(f);
                Clipboard.SetText(txt);
                ChatWindow.ShowChat(Hub.S.ChatProvider);
                Hub.Notices.Push("Чат", "💬 Скопировано", "Вставьте в чат (Ctrl+V)");
            }
            catch { }
        });
        Zone("\uE8EA", "На телефон", "появится на странице телефона", fs =>
        {
            int n = 0; foreach (var f in fs) if (System.IO.File.Exists(f) && Hub.Shelf.Add(f) != null) n++;
            if (!Hub.Shelf.Running) { Hub.S.ShelfOn = true; Hub.S.Save(); _ = Hub.Shelf.StartAsync(); }
            if (n > 0) Hub.Notices.Push("Полка", "📱 Для телефона", "Откройте «Полку» — там QR-код");
        });
        _dropUi = new Border { CornerRadius = new CornerRadius(26), Visibility = Visibility.Collapsed, Child = uni, Margin = new Thickness(0) };
        _dropUi.SetResourceReference(Border.BackgroundProperty, "Bg");
        ((Grid)Pill.Child).Children.Add(_dropUi);
    }

    void EnterDrop()
    {
        if (_dropUi == null) BuildDropUi();
        _dragSeen = DateTime.Now;
        if (_dropMode) return;
        _dropMode = true;
        _userHidden = false;
        SetOpen(true);
        _h.Target = 150;
        _dropUi.Opacity = 0; _dropUi.Visibility = Visibility.Visible;
        _dropUi.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        _dragTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _dragTimer.Tick -= DragWatch; _dragTimer.Tick += DragWatch; _dragTimer.Start();
    }

    void DragWatch(object s, EventArgs e)
    {
        if ((DateTime.Now - _dragSeen).TotalMilliseconds > 600) ExitDrop();
    }

    void ExitDrop()
    {
        if (!_dropMode) return;
        _dropMode = false; _dragTimer?.Stop();
        _dropUi.Visibility = Visibility.Collapsed;
        _h.Target = ExpH;
    }

    public TabPage MakePage(string name) => _all.First(t => t.name == name).make();

    static readonly Dictionary<string, string> Glyphs8 = new()
    {
        ["Главная"] = "\uE80F", ["Музыка"] = "\uE8D6", ["Погода"] = "\uE706", ["Чат"] = "\uE8BD", ["Почта"] = "\uE715", ["Telegram"] = "\uE724",
        ["Таймер"] = "\uE916", ["Буфер"] = "\uE77F", ["Полка"] = "\uE8B7", ["Заметки"] = "\uE70F", ["Календарь"] = "\uE787", ["Система"] = "\uE7F8",
    };
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    object TabContent(int i)
    {
        var info = _tabs[i]; bool act = i == _tabIndex;
        int n = info.name == "Почта" ? Hub.Mail.TotalUnread : info.name == "Telegram" ? Hub.Telegram.Unread : 0;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var ic = Glyphs8.TryGetValue(info.name, out var g) ? new TextBlock { Text = g, FontFamily = IconFont, FontSize = 14 } : new TextBlock { Text = info.icon, FontSize = 14 };
        ic.VerticalAlignment = VerticalAlignment.Center;
        ic.SetResourceReference(TextBlock.ForegroundProperty, act ? "Fg" : "FgDim");
        sp.Children.Add(ic);
        if (act)
        {
            var l = new TextBlock { Text = info.name, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            l.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); sp.Children.Add(l);
        }
        if (n > 0)
        {
            var bd = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                                  Child = new TextBlock { Text = n > 99 ? "99+" : n.ToString(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White } };
            bd.SetResourceReference(Border.BackgroundProperty, "Accent");
            sp.Children.Add(bd);
        }
        return sp;
    }

    void BuildBarRight()
    {
        BarRight.Children.Clear();
        Button Mk(string glyph, string tip, Action a)
        {
            var b = new Button { Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14 }, ToolTip = tip, Background = Brushes.Transparent,
                                 Style = (Style)Application.Current.FindResource("IconBtn"), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 1, 0) };
            ((TextBlock)b.Content).SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
            b.Background = Brushes.Transparent;
            b.Click += (_, _) => a();
            return b;
        }
        BarRight.Children.Add(Mk("\uE793", "Светлая / тёмная тема", ToggleTheme));
        BarRight.Children.Add(Mk("\uE7F4", "Стеклянный вид", ToggleGlass));
        BarRight.Children.Add(Mk("\uE713", "Настройки", () => SettingsWindow.ShowIt(this)));
    }

    void BuildTabs()
    {
        _all.Add(("🏠", "Главная", () => new HomeTab(this)));
        _all.Add(("🎵", "Музыка", () => new MusicTab()));
        _all.Add(("☀️", "Погода", () => new WeatherTab()));
        _all.Add(("💬", "Чат", () => new ChatTab()));
        _all.Add(("📧", "Почта", () => new MailTab()));
        _all.Add(("✈️", "Telegram", () => new TelegramTab()));
        _all.Add(("⏱", "Таймер", () => new TimerTab()));
        _all.Add(("📋", "Буфер", () => new ClipboardTab()));
        _all.Add(("🗄", "Полка", () => new ShelfTab()));
        _all.Add(("📝", "Заметки", () => new NotesTab()));
        _all.Add(("📅", "Календарь", () => new CalendarTab()));
        _all.Add(("🖥", "Система", () => new SystemTab()));
        _all.Add(("🎄", "Праздники", () => new HolidayTab()));
        _all.Add(("🧩", "Вкладки", () => new CustomizeTab(this)));
        _all.Add(("⚙️", "Настройки", () => new SettingsTab(this)));
        ApplyTabs();
    }

    /// <summary>Все вкладки в выбранном пользователем порядке; «Вкладки» и «Настройки» всегда в конце.</summary>
    public List<(string icon, string name)> TabInfos()
    {
        var movable = _all.Where(t => !Pinned.Contains(t.name)).ToList();
        var order = Hub.S.TabOrder;
        var sorted = movable.OrderBy(t => { int i = order.IndexOf(t.name); return i < 0 ? 1000 + movable.IndexOf(t) : i; }).ToList();
        foreach (var p in Pinned) sorted.Add(_all.First(t => t.name == p));
        return sorted.Select(t => (t.icon, t.name)).ToList();
    }

    public static bool IsPinnedTab(string name) => Pinned.Contains(name);

    public void ApplyTabs()
    {
        string cur = _tabIndex >= 0 && _tabIndex < _tabs.Count ? _tabs[_tabIndex].name : null;
        _tabs.Clear(); _tabBtns.Clear(); TabBar.Children.Clear();
        foreach (var info in TabInfos())
        {
            if (Hub.S.HiddenTabs.Contains(info.name) && !Pinned.Contains(info.name)) continue;
            if (InSettings.Contains(info.name)) continue;
            _tabs.Add(_all.First(t => t.name == info.name));
        }
        for (int i = 0; i < _tabs.Count; i++)
        {
            int idx = i;
            var b = new Button { ToolTip = _tabs[i].name, Padding = new Thickness(9, 5, 9, 5), FontSize = 14, Background = Brushes.Transparent,
                                 Style = (Style)Application.Current.FindResource("IconBtn"), Margin = new Thickness(0, 0, 1, 0) };
            b.Click += (_, _) => ShowTab(idx);
            TabBar.Children.Add(b); _tabBtns.Add(b);
        }
        BuildBarRight();
        int keep = cur == null ? 0 : _tabs.FindIndex(t => t.name == cur);
        _tabIndex = keep < 0 ? 0 : keep;
        if (_open) ShowTab(_tabIndex);
        UpdateBadges();
    }

    void UpdateBadges()
    {
        for (int i = 0; i < _tabs.Count && i < _tabBtns.Count; i++) _tabBtns[i].Content = TabContent(i);
    }

    TabPage Page(int i)
    {
        var name = _tabs[i].name;
        if (!_pages.TryGetValue(name, out var p)) { p = _tabs[i].make(); _pages[name] = p; }
        return p;
    }

    void ShowTab(int i)
    {
        _tabIndex = i;
        Host.Children.Clear();
        var p = Page(i);
        Host.Children.Add(p);
        p.OnShown();
        for (int k = 0; k < _tabBtns.Count; k++)
        {
            if (k == i) _tabBtns[k].SetResourceReference(Control.BackgroundProperty, "CardHover");
            else _tabBtns[k].Background = Brushes.Transparent;
        }
        UpdateBadges();
        // мягкое появление страницы: лёгкий сдвиг снизу + проявление
        var tt = new TranslateTransform(0, 10); Host.RenderTransform = tt;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        tt.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        Host.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        _charE.ShowEmotion(Mood.Laugh, 0.5);
    }

    // ───────── горячие клавиши ─────────
    const int HkToggle = 1, HkShot = 2, HkTheme = 3, HkOpen = 4;
    void RegisterHotkeys(IntPtr h)
    {
        uint m = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        foreach (var (id, vk) in new[] { (HkToggle, 'I'), (HkShot, 'S'), (HkTheme, 'T'), (HkOpen, 'O') })
            if (!Native.RegisterHotKey(h, id, m, vk))
                AppSettings.Log($"hotkey Ctrl+Alt+{vk} занято другой программой");
    }
    void UnregisterHotkeys()
    {
        var h = new WindowInteropHelper(this).Handle;
        for (int i = 1; i <= 4; i++) Native.UnregisterHotKey(h, i);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY)
        {
            handled = true;
            switch (w.ToInt32())
            {
                case HkToggle: ToggleHidden(); break;
                case HkShot: Dispatcher.BeginInvoke(() => ScreenshotWindow.StartRegion()); break;
                case HkTheme: ToggleTheme(); break;
                case HkOpen: OpenIsland(); break;
            }
        }
        return IntPtr.Zero;
    }
}
