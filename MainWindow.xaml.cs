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
    readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(350) };

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
        _charC.Kind = _charE.Kind = Hub.S.Character;
        CompactRight.Content = _rightText;

        BuildTabs();
        Loaded += OnLoaded;
        MouseEnter += (_, _) => { _leave.Stop(); if (!Hub.S.OpenOnClick) SetOpen(true); };
        MouseLeave += (_, _) => { if (!_dragging) _leave.Start(); };
        _leave.Tick += (_, _) => { _leave.Stop(); if (!IsMouseOver && !_dragging) SetOpen(false); };
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
        Left = _centerX - CompactW / 2; Top = _topBase + _y.Value;

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
        if (_w.Settled && _h.Settled && _y.Settled && _r.Settled) return;

        _w.Step(dt); _h.Step(dt); _y.Step(dt); _r.Step(dt);
        double w = Math.Max(60, _w.Value), h = Math.Max(24, _h.Value);
        Width = w; Height = h;
        Left = _centerX - w / 2; Top = _topBase + _y.Value;
        ApplyShape();

        double t = Math.Clamp((_w.Value - CompactW) / (ExpW - CompactW), 0, 1);
        Expanded.Opacity = Math.Clamp((t - 0.45) / 0.55, 0, 1);
        Compact.Opacity = Math.Clamp(1 - t * 2.4, 0, 1);
        Compact.IsHitTestVisible = t < 0.5;
        Expanded.IsHitTestVisible = t > 0.8;
    }

    void ApplyShape()
    {
        double r = Math.Max(10, _r.Value);
        Pill.CornerRadius = Hub.S.EdgeMode ? new CornerRadius(0, 0, r, r) : new CornerRadius(r);
    }

    void SetOpen(bool open)
    {
        if (open && (_userHidden || _busyFullscreen)) return;
        if (_open == open) return;
        _open = open;
        if (open)
        {
            _w.Target = ExpW; _h.Target = ExpH; _r.Target = 28;
            _w.Stiffness = _h.Stiffness = 240; _w.Damping = _h.Damping = 17;   // мягкий «капельный» овершут
            ShowTab(_tabIndex);
            _charE.ShowEmotion(Mood.Love, 1.2);
            Hub.Notices.MarkRead();
            if (Hub.S.OpenOnClick) Activate();
        }
        else
        {
            var (w, h) = CompactSize();
            _w.Target = w; _h.Target = h; _r.Target = h / 2;
            _w.Stiffness = _h.Stiffness = 320; _w.Damping = _h.Damping = 24;
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
        if (!_open) { var (w2, h2) = CompactSize(); _w.Target = w2; _h.Target = h2; _r.Target = h2 / 2; }
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
        // персонаж следит за курсором
        Native.GetCursorPos(out var p);
        var src = PresentationSource.FromVisual(this);
        double sx = src?.CompositionTarget.TransformFromDevice.M11 ?? 1;
        double dx = p.X * sx - (Left + 30), dy = p.Y * sx - (Top + 20);
        double len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        foreach (var c in new[] { _charC, _charE })
        {
            c.LookX = Math.Clamp(dx / 200, -1, 1); c.LookY = Math.Clamp(dy / 200, -1, 1);
            c.Sleeping = !_open && !Hub.Media.IsPlaying && (DateTime.Now - _lastMove).TotalMinutes > 5;
        }
        if (Math.Abs(dx) + Math.Abs(dy) > 3) _lastMove = DateTime.Now;

        // открытие по наведению даже если окно уже сжалось (мышь вне окна, но над зоной)
        if (!Hub.S.OpenOnClick && !_open && !_userHidden && !_busyFullscreen)
        {
            if (p.X * sx >= Left && p.X * sx <= Left + Width && p.Y * sx >= Top - 2 && p.Y * sx <= Top + Height + 2 && IsMouseOver) SetOpen(true);
        }

        // клик мимо открытого острова закрывает его (режим наведения)
        if (_open && !IsMouseOver && !_dragging && (Native.GetAsyncKeyState(0x01) & 0x8000) != 0) SetOpen(false);

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
        _y.Target = hide ? -(Height + 12) : 0;
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
        _dragging = true; _dragStart = PointToScreen(e.GetPosition(this)); _dragCx = _centerX; _dragTop = _topBase;
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

    void OnDragMove(object s, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = PointToScreen(e.GetPosition(this));
        var src = PresentationSource.FromVisual(this);
        double k = src?.CompositionTarget.TransformFromDevice.M11 ?? 1;
        _centerX = _dragCx + (p.X - _dragStart.X) * k;
        _topBase = Math.Max(-8, _dragTop + (p.Y - _dragStart.Y) * k);
        if (Hub.S.EdgeMode) _topBase = 0;
        Left = _centerX - Width / 2; Top = _topBase + _y.Value;
    }

    void OnUp(object s, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false; ReleaseMouseCapture();
        var p = PointToScreen(e.GetPosition(this));
        var src = PresentationSource.FromVisual(this);
        double k = src?.CompositionTarget.TransformFromDevice.M11 ?? 1;
        // смахнули вверх — спрятать
        if ((_dragStart.Y - p.Y) * k > 40 && !Hub.S.EdgeMode) { _topBase = Hub.S.Top ?? 8; _centerX = _dragCx; ToggleHidden(); return; }
        Hub.S.Left = _centerX; if (!Hub.S.EdgeMode) Hub.S.Top = _topBase; Hub.S.Save();
        if (!IsMouseOver) _leave.Start();
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
        ApplyShape(); Top = _topBase + _y.Value;
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
