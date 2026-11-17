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
        Theme.Apply();
        CompactChar.Content = _charC; ExpChar.Content = _charE;
        Expanded.RenderTransformOrigin = new Point(0.5, 0); Expanded.RenderTransform = _expScale;
        _charC.Kind = _charE.Kind = Hub.S.Character;
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
        RegisterHotkeys(h);

        var wa = SystemParameters.WorkArea;
        _centerX = Hub.S.Left ?? SystemParameters.PrimaryScreenWidth / 2;
        _topBase = Hub.S.EdgeMode ? 0 : (Hub.S.Top ?? 8);
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
            _ => (240, CompactH),
        };
    }

    // ───────── живые активности ─────────
    enum Activity { Idle, Music, Timer, Notice }

    List<Activity> Activities()
    {
        var l = new List<Activity>();
        if (_notice != null && DateTime.Now < _noticeUntil) l.Add(Activity.Notice);
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

    void ShowNotice(IslandNotice n)
    {
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
                _rightText.Text = w == null ? "" : $"{w.Icon} {Math.Round(w.Temp)}°";
                break;
        }
        _rightText.Foreground = (Brush)Application.Current.Resources["Fg"];
        _charC.Dancing = Hub.Media.IsPlaying;
        _charE.Dancing = Hub.Media.IsPlaying;
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
            if (fs != _busyFullscreen) { _busyFullscreen = fs; UpdateY(); }
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
            if (d is Button or TextBox or Slider or System.Windows.Controls.Primitives.ScrollBar or ComboBox or CheckBox or System.Windows.Controls.Primitives.ToggleButton
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
        _topBase = Math.Max(-8, _dragTop + (p.Y - _dragStart.Y));
        if (Hub.S.EdgeMode) _topBase = 0;
        ApplyBounds();
    }

    void OnUp(object s, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false; ReleaseMouseCapture();
        var p = CursorDip();
        // смахнули вверх — спрятать
        if ((_dragStart.Y - p.Y) > 40 && !Hub.S.EdgeMode) { _topBase = Hub.S.Top ?? 8; _centerX = _dragCx; ToggleHidden(); return; }
        Hub.S.Left = _centerX; if (!Hub.S.EdgeMode) Hub.S.Top = _topBase; Hub.S.Save();
    }

    // ───────── правое меню ─────────
    void ShowMenu()
    {
        var m = new ContextMenu();
        void Add(string t, Action a) { var i = new MenuItem { Header = t }; i.Click += (_, _) => a(); m.Items.Add(i); }
        Add("Спрятать / показать", ToggleHidden);
        Add(Hub.S.DarkTheme ? "Светлая тема" : "Тёмная тема", ToggleTheme);
        Add("Настройки", () => OpenIsland(_tabs.Count - 1));
        m.Items.Add(new Separator());
        Add("Выход", () => { Hub.S.Save(); Application.Current.Shutdown(); });
        m.IsOpen = true;
    }

    public void ToggleTheme()
    {
        Hub.S.DarkTheme = !Hub.S.DarkTheme; Hub.S.Save(); Theme.Apply(); RefreshCompact();
        if (_open) ShowTab(_tabIndex);
    }

    public void ApplyCharacter() { _charC.Kind = _charE.Kind = Hub.S.Character; }
    public void ApplyEdgeMode()
    {
        _topBase = Hub.S.EdgeMode ? 0 : (Hub.S.Top ?? 8);
        ApplyShape(); ApplyBounds();
    }

    // ───────── вкладки ─────────
    void BuildTabs()
    {
        _tabs.Add(("🏠", "Главная", () => new HomeTab(this)));
        _tabs.Add(("🎵", "Музыка", () => new MusicTab()));
        _tabs.Add(("☀️", "Погода", () => new WeatherTab()));
        _tabs.Add(("💬", "Чат", () => new ChatTab()));
        _tabs.Add(("⏱", "Таймер", () => new TimerTab()));
        _tabs.Add(("📋", "Буфер", () => new ClipboardTab()));
        _tabs.Add(("📝", "Заметки", () => new NotesTab()));
        _tabs.Add(("📅", "Календарь", () => new CalendarTab()));
        _tabs.Add(("🖥", "Система", () => new SystemTab()));
        _tabs.Add(("⚙️", "Настройки", () => new SettingsTab(this)));

        for (int i = 0; i < _tabs.Count; i++)
        {
            int idx = i;
            var b = new Button { Content = _tabs[i].icon, ToolTip = _tabs[i].name, Padding = new Thickness(8, 4, 8, 4), FontSize = 14,
                                 Style = (Style)Application.Current.FindResource("IconBtn"), Margin = new Thickness(0, 0, 2, 0) };
            b.Click += (_, _) => ShowTab(idx);
            TabBar.Children.Add(b); _tabBtns.Add(b);
        }
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
            if (k == i) _tabBtns[k].SetResourceReference(Control.BackgroundProperty, "Accent");
            else _tabBtns[k].SetResourceReference(Control.BackgroundProperty, "Card");
        }
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
