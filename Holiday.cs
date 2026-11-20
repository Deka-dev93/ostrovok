using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ostrovok.Views;

/// <summary>Праздничное оформление: когда включено, как включается и какие у него части.</summary>
public static class Holiday
{
    public static readonly string[] Chars = { "Snowman", "Santa", "Bullfinch", "Gingerbread", "Reindeer" };
    public static string NameOf(string kind) => kind switch
    {
        "Snowman" => "Снеговик", "Santa" => "Дед Мороз", "Bullfinch" => "Снегирь", "Gingerbread" => "Пряник", "Reindeer" => "Олень", _ => "Свой"
    };

    public static event Action Changed;
    static SnowWindow _snow;
    static DispatcherTimer _timer;
    public static bool Suspended { get; set; }

    public static bool Active
    {
        get
        {
            var m = Hub.S.HolidayMode;
            if (m == "always") return true;
            if (m == "off") return false;
            var d = DateTime.Today;
            return d.Month == 12 || (d.Month == 1 && d.Day <= 14);
        }
    }

    public static string EffectiveCharacter => Active && !string.IsNullOrEmpty(Hub.S.HolidayChar) ? Hub.S.HolidayChar : Hub.S.Character;

    public static void Start()
    {
        Apply();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += (_, _) => Apply();
        _timer.Start();
    }

    public static void Apply()
    {
        bool snow = Active && Hub.S.HolidaySnowScreen && !Suspended;
        if (snow)
        {
            _snow ??= new SnowWindow();
            _snow.Amount = Hub.S.SnowAmount;
            if (!_snow.IsVisible) _snow.Show();
        }
        else if (_snow != null && _snow.IsVisible) _snow.Hide();
        Changed?.Invoke();
    }
}

/// <summary>Точки и контур скруглённого прямоугольника (радиусы сверху и снизу могут быть разными).</summary>
static class RoundedPath
{
    public static StreamGeometry Geometry(double w, double h, double rt, double rb, double inset)
    {
        var (x0, y0, x1, y1) = (inset, inset, w - inset, h - inset);
        rt = Math.Clamp(rt - inset, 0, Math.Min(x1 - x0, y1 - y0) / 2); rb = Math.Clamp(rb - inset, 0, Math.Min(x1 - x0, y1 - y0) / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0 + rt, y0), true, true);
            c.LineTo(new Point(x1 - rt, y0), true, false);
            if (rt > 0) c.ArcTo(new Point(x1, y0 + rt), new Size(rt, rt), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x1, y1 - rb), true, false);
            if (rb > 0) c.ArcTo(new Point(x1 - rb, y1), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x0 + rb, y1), true, false);
            if (rb > 0) c.ArcTo(new Point(x0, y1 - rb), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x0, y0 + rt), true, false);
            if (rt > 0) c.ArcTo(new Point(x0 + rt, y0), new Size(rt, rt), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>n точек, равномерно по периметру, по часовой стрелке.</summary>
    public static Point[] Sample(double w, double h, double rt, double rb, double inset, int n)
    {
        var (x0, y0, x1, y1) = (inset, inset, w - inset, h - inset);
        rt = Math.Clamp(rt - inset, 0, Math.Min(x1 - x0, y1 - y0) / 2); rb = Math.Clamp(rb - inset, 0, Math.Min(x1 - x0, y1 - y0) / 2);
        double top = Math.Max(0, x1 - x0 - 2 * rt), side = Math.Max(0, y1 - y0 - rt - rb), bot = Math.Max(0, x1 - x0 - 2 * rb);
        double[] len = { top, Math.PI / 2 * rt, side, Math.PI / 2 * rb, bot, Math.PI / 2 * rb, side, Math.PI / 2 * rt };
        double total = len.Sum();
        var res = new Point[n];
        for (int k = 0; k < n; k++)
        {
            double d = total * k / n; int s = 0;
            while (s < 7 && d > len[s]) { d -= len[s]; s++; }
            double f = len[s] > 0 ? d / len[s] : 0;
            res[k] = s switch
            {
                0 => new Point(x0 + rt + top * f, y0),
                1 => Arc(x1 - rt, y0 + rt, rt, -90 + 90 * f),
                2 => new Point(x1, y0 + rt + side * f),
                3 => Arc(x1 - rb, y1 - rb, rb, 90 * f),
                4 => new Point(x1 - rb - bot * f, y1),
                5 => Arc(x0 + rb, y1 - rb, rb, 90 + 90 * f),
                6 => new Point(x0, y1 - rb - side * f),
                _ => Arc(x0 + rt, y0 + rt, rt, 180 + 90 * f),
            };
        }
        return res;
    }

    static Point Arc(double cx, double cy, double r, double deg) =>
        new(cx + Math.Cos(deg * Math.PI / 180) * r, cy + Math.Sin(deg * Math.PI / 180) * r);
}

/// <summary>Гирлянда вокруг острова: мигает, бежит волной или пульсирует под музыку.</summary>
public class Garland : FrameworkElement
{
    static readonly Color[] Cols =
    {
        Color.FromRgb(255, 77, 77), Color.FromRgb(61, 220, 132), Color.FromRgb(77, 163, 255), Color.FromRgb(255, 216, 77), Color.FromRgb(255, 122, 217)
    };
    static readonly Brush[] Core = Cols.Select(c => { var b = new SolidColorBrush(c); b.Freeze(); return (Brush)b; }).ToArray();
    static readonly Pen Cable = MakeCable();
    static Pen MakeCable() { var p = new Pen(new SolidColorBrush(Color.FromArgb(120, 20, 40, 30)), 1.1); p.Freeze(); return p; }

    double _rt = 18, _rb = 18, _t, _acc;
    DateTime _last = DateTime.Now;
    Point[] _pts, _cable; Size _ptsSize; double _ptsRt, _ptsRb;
    public string Mode { get; set; } = "Blink";
    public bool Music { get; set; }

    public Garland()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { _last = DateTime.Now; CompositionTarget.Rendering += OnTick; };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnTick;
    }

    public void SetRadii(double rt, double rb) { _rt = rt; _rb = rb; }

    void OnTick(object s, EventArgs e)
    {
        var now = DateTime.Now; double dt = Math.Min((now - _last).TotalSeconds, 0.1); _last = now;
        if (!IsVisible || Mode == "Off") return;
        _t += dt; _acc += dt;
        if (_acc < 1.0 / 30) return;
        _acc = 0; InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 16 || Mode == "Off") return;
        if (_pts == null || _ptsSize.Width != w || _ptsSize.Height != h || _ptsRt != _rt || _ptsRb != _rb)
        {
            int n = Math.Max(14, (int)((2 * (w + h)) / 14));
            _pts = RoundedPath.Sample(w, h, _rt, _rb, 3.2, n);
            _cable = RoundedPath.Sample(w, h, _rt, _rb, 3.2, n * 3);
            _ptsSize = new Size(w, h); _ptsRt = _rt; _ptsRb = _rb;
        }
        for (int i = 0; i < _cable.Length; i++) dc.DrawLine(Cable, _cable[i], _cable[(i + 1) % _cable.Length]);

        double beat = 0.35;
        if (Mode == "Music")
            beat = Music ? Math.Pow(0.5 + 0.5 * Math.Sin(_t * 8.4), 3) * 0.65 + 0.25 * (0.5 + 0.5 * Math.Sin(_t * 3.1)) : 0.3;

        for (int i = 0; i < _pts.Length; i++)
        {
            double b = Mode switch
            {
                "Wave" => 0.22 + 0.78 * Math.Max(0, Math.Sin(_t * 4.2 - i * 0.55)),
                "Music" => 0.22 + 0.78 * Math.Clamp(beat * (0.55 + 0.45 * Math.Sin(i * 0.8 + _t * 2)), 0, 1),
                _ => 0.28 + 0.72 * (0.5 + 0.5 * Math.Sin(_t * 3.2 + (i % 2) * Math.PI + (i % 5) * 0.4)),
            };
            var col = Core[i % Core.Length];
            var c = ((SolidColorBrush)col).Color;
            // тусклая лампочка — тёмная, яркая — с ореолом
            var glow = new SolidColorBrush(Color.FromArgb((byte)(95 * b), c.R, c.G, c.B));
            if (b > 0.45) dc.DrawEllipse(glow, null, _pts[i], 4.4 * b + 1.5, 4.4 * b + 1.5);
            var core = new SolidColorBrush(Color.FromArgb((byte)(120 + 135 * b), c.R, c.G, c.B));
            dc.DrawEllipse(core, null, _pts[i], 2.3, 2.3);
        }
    }
}

/// <summary>Сугроб на верхнем крае острова (обрезается по скруглению острова).</summary>
public class SnowCap : FrameworkElement
{
    double _rt = 18, _rb = 18;
    static readonly Brush Fill = MakeFill();
    static Brush MakeFill()
    {
        var b = new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(214, 226, 244), 90);
        b.Freeze(); return b;
    }

    public SnowCap()
    {
        IsHitTestVisible = false;
        SizeChanged += (_, _) => UpdateClip();
    }

    public void SetRadii(double rt, double rb) { if (_rt == rt && _rb == rb) return; _rt = rt; _rb = rb; UpdateClip(); }

    void UpdateClip()
    {
        if (ActualWidth < 4 || ActualHeight < 4) return;
        Clip = RoundedPath.Geometry(ActualWidth, ActualHeight, _rt, _rb, 0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 20) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w, 0), true, false);
            for (double x = w; x >= 0; x -= 3)
            {
                double y = 5.2 + Math.Sin(x * 0.19) * 1.5 + Math.Sin(x * 0.067 + 1.3) * 2.0 + (Math.Sin(x * 0.31 + 2) > 0.8 ? 2.5 : 0);
                c.LineTo(new Point(x, y), true, true);
            }
        }
        g.Freeze();
        dc.DrawGeometry(Fill, null, g);
    }
}

/// <summary>Снег по всему экрану. Окно прозрачное для мыши: все щелчки проходят насквозь.</summary>
public class SnowWindow : Window
{
    readonly SnowCanvas _c = new();
    public double Amount { get => _c.Amount; set => _c.Amount = value; }

    public SnowWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0; Top = 0; Width = SystemParameters.PrimaryScreenWidth; Height = SystemParameters.PrimaryScreenHeight;
        Content = _c;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | 0x20 /*WS_EX_TRANSPARENT*/ | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        };
    }
}

class SnowCanvas : FrameworkElement
{
    class Flake { public double X, Y, R, V, Ph, Sw; public int B; }
    readonly List<Flake> _f = new();
    readonly Random _rnd = new();
    static readonly Brush[] Soft = new[] { 90, 140, 190, 235 }.Select(a => { var b = new SolidColorBrush(Color.FromArgb((byte)a, 255, 255, 255)); b.Freeze(); return (Brush)b; }).ToArray();
    DateTime _last = DateTime.Now; double _acc, _t;
    public double Amount { get; set; } = 100;

    public SnowCanvas()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { _last = DateTime.Now; CompositionTarget.Rendering += OnTick; };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnTick;
    }

    void OnTick(object s, EventArgs e)
    {
        var now = DateTime.Now; double dt = Math.Min((now - _last).TotalSeconds, 0.1); _last = now;
        if (!IsVisible) return;
        _acc += dt; _t += dt;
        if (_acc < 1.0 / 24) return;       // 24 кадра в секунду — хватает для снега и почти не грузит процессор
        double step = _acc; _acc = 0;

        double w = ActualWidth, h = ActualHeight;
        if (w < 50 || h < 50) return;
        int want = (int)(w * h / 15000 * Amount / 100);
        while (_f.Count < want) _f.Add(New(w, h, true));
        if (_f.Count > want) _f.RemoveRange(want, _f.Count - want);

        foreach (var f in _f)
        {
            f.Y += f.V * step;
            f.X += Math.Sin(_t * 0.9 + f.Ph) * f.Sw * step;
            if (f.Y > h + 6) { var n = New(w, h, false); f.X = n.X; f.Y = n.Y; f.R = n.R; f.V = n.V; f.Ph = n.Ph; f.Sw = n.Sw; f.B = n.B; }
        }
        InvalidateVisual();
    }

    Flake New(double w, double h, bool anywhere)
    {
        double r = 1 + _rnd.NextDouble() * 2.4;
        return new Flake { X = _rnd.NextDouble() * w, Y = anywhere ? _rnd.NextDouble() * h : -6, R = r, V = 22 + r * 14 + _rnd.NextDouble() * 18,
                           Ph = _rnd.NextDouble() * 6.28, Sw = 8 + _rnd.NextDouble() * 14, B = Math.Min(3, (int)(r * 1.2)) };
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var f in _f) dc.DrawEllipse(Soft[f.B], null, new Point(f.X, f.Y), f.R, f.R);
    }
}
