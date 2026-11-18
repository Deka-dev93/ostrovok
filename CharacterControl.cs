using System.Windows;
using System.Windows.Media;

namespace Ostrovok.Views;

public enum Mood { Normal, Love, Angry, Surprised, Sad, Dizzy, Laugh, Cool }

/// <summary>
/// Маленький персонаж, который живёт в пилюле.
/// Движение (взгляд, дыхание, танец, моргание, «хлопок» при эмоции) считается плавно на каждом кадре экрана,
/// а контур дрожит «от руки» с частотой 12 кадров/с — это и есть рисованный стиль.
/// </summary>
public class CharacterControl : FrameworkElement
{
    public static readonly string[] Kinds = { "Fox", "Cat", "Bear", "Ghost", "Robot", "Spark", "Slime" };

    string _kind = "Fox";
    public string Kind { get => _kind; set { if (_kind != value) { _kind = value; _palKind = null; } } }
    public bool Dancing { get; set; }
    public bool Sleeping { get; set; }
    public bool Thinking { get; set; }
    public double LookX { get; set; }
    public double LookY { get; set; }

    Mood _mood = Mood.Normal;
    DateTime _moodUntil;
    readonly Random _rnd = new();

    // время и «рисованные» кадры
    DateTime _last = DateTime.Now;
    double _time, _acc;
    int _frame;                       // 12 к/с — только для дрожания контура

    // плавные состояния
    double _lx, _ly;                  // сглаженный взгляд
    double _dance, _sleep, _think;    // 0..1, плавно включаются и выключаются
    double _popT = 99;                // время с начала эмоции — для «хлопка» (сжатие-растяжение)
    double _blinkAt = 3, _blinkT = -1;

    // кэш кисти и геометрии (геометрия живёт один «рисованный» кадр)
    string _palKind;
    Brush _body, _ink;
    Pen _pen, _thin;
    readonly Dictionary<int, StreamGeometry> _geo = new();
    int _geoFrame = -1;

    static readonly Mood[] Moods = { Mood.Love, Mood.Angry, Mood.Surprised, Mood.Sad, Mood.Dizzy, Mood.Laugh, Mood.Cool };

    public CharacterControl()
    {
        IsHitTestVisible = true;
        Cursor = System.Windows.Input.Cursors.Hand;
        Loaded += (_, _) => { _last = DateTime.Now; CompositionTarget.Rendering += OnTick; };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnTick;
        MouseLeftButtonUp += (_, _) => ShowEmotion(Moods[_rnd.Next(Moods.Length)]);
    }

    public void ShowEmotion(Mood m, double seconds = 2.2)
    {
        _mood = m; _moodUntil = DateTime.Now.AddSeconds(seconds); _popT = 0;
    }

    // ───────── кадр ─────────
    void OnTick(object s, EventArgs e)
    {
        var now = DateTime.Now;
        double dt = Math.Min((now - _last).TotalSeconds, 0.05); _last = now;
        if (dt <= 0 || !IsVisible) return;

        _time += dt;
        _acc += dt;
        while (_acc >= 1.0 / 12) { _acc -= 1.0 / 12; _frame++; }

        if (_mood != Mood.Normal && now > _moodUntil) _mood = Mood.Normal;

        // сглаживание: экспоненциальное, не зависит от частоты кадров
        double look = 1 - Math.Exp(-dt * 11);
        _lx += (LookX - _lx) * look; _ly += (LookY - _ly) * look;
        Ease(ref _dance, Dancing ? 1 : 0, dt, 5);
        Ease(ref _sleep, Sleeping ? 1 : 0, dt, 2.5);
        Ease(ref _think, Thinking ? 1 : 0, dt, 5);
        _popT += dt;

        // моргание: плавное закрытие и открытие за 0.16 с
        if (_blinkT >= 0) { _blinkT += dt; if (_blinkT > 0.16) _blinkT = -1; }
        _blinkAt -= dt;
        if (_blinkAt <= 0 && _blinkT < 0) { _blinkT = 0; _blinkAt = 2 + _rnd.NextDouble() * 4; }

        InvalidateVisual();
    }

    static void Ease(ref double v, double target, double dt, double speed) =>
        v += (target - v) * (1 - Math.Exp(-dt * speed));

    // ───────── краски ─────────
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    void EnsurePalette()
    {
        if (_palKind == Kind) return;
        _palKind = Kind;
        var (body, dark) = Kind switch
        {
            "Cat" => (C("#C9CED6"), C("#6B7280")),
            "Bear" => (C("#C79A6B"), C("#6B4A2B")),
            "Ghost" => (C("#F1F3FF"), C("#8E96C9")),
            "Robot" => (C("#8FD3FF"), C("#2F6E99")),
            "Spark" => (C("#E8825B"), C("#A8482A")),
            "Slime" => (C("#6EE7A8"), C("#1F7A55")),
            _ => (C("#FF9B4A"), C("#8F4A12")),
        };
        _body = new SolidColorBrush(body); _body.Freeze();
        _ink = new SolidColorBrush(dark); _ink.Freeze();
        _pen = new Pen(_ink, 2.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        _thin = new Pen(_ink, 2.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        _pen.Freeze(); _thin.Freeze();
    }

    // ───────── «дрожащие» формы (кэшируются на один рисованный кадр) ─────────
    StreamGeometry Cached(int key, Func<StreamGeometry> make)
    {
        if (_geoFrame != _frame) { _geo.Clear(); _geoFrame = _frame; }
        if (!_geo.TryGetValue(key, out var g)) { g = make(); _geo[key] = g; }
        return g;
    }

    StreamGeometry Wobble(double cx, double cy, double rx, double ry, int n = 26, double amp = 0.9)
    {
        int key = HashCode.Combine(1, cx, cy, rx, ry, n, amp);
        return Cached(key, () =>
        {
            var g = new StreamGeometry();
            var rnd = new Random(_frame * 31 + (int)(cx * 7 + cy * 13 + rx));
            using (var c = g.Open())
            {
                for (int i = 0; i < n; i++)
                {
                    double a = i * Math.PI * 2 / n;
                    var p = new Point(cx + Math.Cos(a) * rx + (rnd.NextDouble() - .5) * amp * 2,
                                      cy + Math.Sin(a) * ry + (rnd.NextDouble() - .5) * amp * 2);
                    if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
                }
            }
            g.Freeze();
            return g;
        });
    }

    StreamGeometry Poly(bool closed, params (double x, double y)[] pts)
    {
        var hc = new HashCode(); hc.Add(2); hc.Add(closed);
        foreach (var p in pts) { hc.Add(p.x); hc.Add(p.y); }
        return Cached(hc.ToHashCode(), () =>
        {
            var g = new StreamGeometry();
            var rnd = new Random(_frame * 17 + pts.Length);
            using (var c = g.Open())
            {
                for (int i = 0; i < pts.Length; i++)
                {
                    var p = new Point(pts[i].x + (rnd.NextDouble() - .5) * 1.4, pts[i].y + (rnd.NextDouble() - .5) * 1.4);
                    if (i == 0) c.BeginFigure(p, closed, closed); else c.LineTo(p, true, true);
                }
            }
            g.Freeze();
            return g;
        });
    }

    // ───────── рисование ─────────
    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side < 4) return;
        EnsurePalette();

        dc.PushTransform(new TranslateTransform((ActualWidth - side) / 2, (ActualHeight - side) / 2));
        dc.PushTransform(new ScaleTransform(side / 100, side / 100));

        // дыхание ↔ танец (плавный переход), наклон головы при «думании», хлопок при эмоции
        double bob = Math.Sin(_time * 2) * 1.2 * (1 - _dance) + Math.Sin(_time * 9) * 5 * _dance;
        double tilt = Math.Sin(_time * 4.5) * 9 * _dance - 6 * _think;
        if (_mood == Mood.Laugh) bob += Math.Sin(_time * 30) * 1.5;
        double pop = 0.2 * Math.Exp(-_popT * 6) * Math.Sin(_popT * 18);
        double breath = 1 + Math.Sin(_time * 2) * 0.012;

        dc.PushTransform(new TranslateTransform(0, bob));
        dc.PushTransform(new RotateTransform(tilt, 50, 85));
        dc.PushTransform(new ScaleTransform((1 + pop) * breath, (1 - pop * 0.7) * breath, 50, 88));

        if (Kind == "Spark") DrawSpark(dc);
        else DrawHead(dc);
        DrawFace(dc);

        if (_sleep > 0.6)
        {
            var ft = new FormattedText("z", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 18, _ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(76, 6 + Math.Sin(_time * 2) * 3));
        }
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }

    void DrawHead(DrawingContext dc)
    {
        switch (Kind)
        {
            case "Fox":
                dc.DrawGeometry(_body, _pen, Poly(true, (17, 44), (22, 8), (44, 28)));
                dc.DrawGeometry(_body, _pen, Poly(true, (83, 44), (78, 8), (56, 28)));
                dc.DrawGeometry(Brushes.White, null, Poly(true, (30, 22), (26, 16), (38, 25)));
                break;
            case "Cat":
                dc.DrawGeometry(_body, _pen, Poly(true, (16, 46), (20, 10), (44, 28)));
                dc.DrawGeometry(_body, _pen, Poly(true, (84, 46), (80, 10), (56, 28)));
                break;
            case "Bear":
                dc.DrawGeometry(_body, _pen, Wobble(22, 28, 11, 11, 14));
                dc.DrawGeometry(_body, _pen, Wobble(78, 28, 11, 11, 14));
                break;
            case "Robot":
                dc.DrawLine(_pen, new Point(50, 20), new Point(50, 8));
                dc.DrawEllipse(new SolidColorBrush(C("#FF6B6B")), _pen, new Point(50, 7), 3.5, 3.5);
                dc.DrawGeometry(_body, _pen, Poly(true, (14, 30), (86, 30), (86, 88), (14, 88)));
                return;
            case "Slime":
                {
                    double sq = Math.Sin(_time * 3) * 2.2;
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(12, 88), true, true);
                        c.BezierTo(new Point(4, 60 - sq), new Point(26, 24 + sq), new Point(50, 24 + sq), true, true);
                        c.BezierTo(new Point(74, 24 + sq), new Point(96, 60 - sq), new Point(88, 88), true, true);
                        c.BezierTo(new Point(70, 93 + sq * 0.5), new Point(30, 93 + sq * 0.5), new Point(12, 88), true, true);
                    }
                    g.Freeze();
                    dc.DrawGeometry(_body, _pen, g);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)), null, new Point(31, 42 + sq), 6.5, 3.5);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 255, 90, 120)), null, new Point(24, 68), 6, 4);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 255, 90, 120)), null, new Point(76, 68), 6, 4);
                    return;
                }
            case "Ghost":
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(14, 90), true, true);
                        c.LineTo(new Point(14, 52), true, true);
                        c.ArcTo(new Point(86, 52), new Size(36, 38), 0, false, SweepDirection.Clockwise, true, true);
                        c.LineTo(new Point(86, 90), true, true);
                        // «рваный» подол колышется
                        double w = Math.Sin(_time * 3) * 1.5;
                        for (int i = 0; i < 4; i++)
                            c.ArcTo(new Point(86 - (i + 1) * 18, 90), new Size(9, 7 + w * (i % 2 == 0 ? 1 : -1)), 0, false,
                                i % 2 == 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, true);
                    }
                    g.Freeze();
                    dc.DrawGeometry(_body, _pen, g);
                    return;
                }
        }
        dc.DrawGeometry(_body, _pen, Wobble(50, 58, 36, 31));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 90, 120)), null, new Point(26, 66), 6, 4);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 90, 120)), null, new Point(74, 66), 6, 4);
    }

    // Claude: оранжевая «искра» — лучи волной вытягиваются и втягиваются
    void DrawSpark(DrawingContext dc)
    {
        var rayPen = new Pen(_body, 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        const int n = 12;
        for (int i = 0; i < n; i++)
        {
            double a = i * Math.PI * 2 / n;
            double len = 26 + 10 * Math.Sin(_time * 3 + i * 0.9) + _dance * 5 * Math.Sin(_time * 9 + i);
            dc.DrawLine(rayPen, new Point(50 + Math.Cos(a) * 10, 55 + Math.Sin(a) * 10),
                                new Point(50 + Math.Cos(a) * len, 55 + Math.Sin(a) * len));
        }
        dc.DrawEllipse(_body, null, new Point(50, 55), 14, 14);
    }

    void DrawFace(DrawingContext dc)
    {
        double ey = Kind == "Spark" ? 53 : (Kind == "Robot" ? 55 : 54);
        double ex = Kind == "Spark" ? 6 : 13;
        double lx = _lx * 3, ly = _ly * 2.2;
        Brush ink = _ink;
        Pen thin = _thin;

        // закрытость глаз: моргание (плавная дуга) или сон
        double blink = _blinkT >= 0 ? Math.Sin(Math.PI * _blinkT / 0.16) : 0;
        double lid = Math.Max(blink, _sleep);

        // ── глаза ──
        switch (_mood)
        {
            case Mood.Love:
                foreach (var s in new[] { -1, 1 }) DrawHeart(dc, 50 + s * ex, ey, 7);
                break;
            case Mood.Laugh:
                foreach (var s in new[] { -1, 1 })
                    dc.DrawGeometry(null, thin, Poly(false, (50 + s * ex - 5, ey + 3), (50 + s * ex, ey - 3), (50 + s * ex + 5, ey + 3)));
                break;
            case Mood.Cool:
                dc.DrawGeometry(ink, null, Poly(true, (50 - ex - 9, ey - 5), (50 + ex + 9, ey - 5), (50 + ex + 7, ey + 5), (50 + ex - 2, ey + 5),
                    (50, ey + 1), (50 - ex + 2, ey + 5), (50 - ex - 7, ey + 5)));
                break;
            case Mood.Dizzy:
                foreach (var s in new[] { -1, 1 })
                {
                    double cx = 50 + s * ex; double ang = _time * 10;
                    var pts = new List<(double, double)>();
                    for (int i = 0; i < 14; i++)
                    {
                        double a = ang + i * 0.8; double r = 1 + i * 0.4;
                        pts.Add((cx + Math.Cos(a) * r, ey + Math.Sin(a) * r));
                    }
                    dc.DrawGeometry(null, thin, Poly(false, pts.ToArray()));
                }
                break;
            default:
                {
                    if (_sleep > 0.85)
                    {
                        foreach (var s in new[] { -1, 1 })
                            dc.DrawLine(thin, new Point(50 + s * ex - 5, ey + 1), new Point(50 + s * ex + 5, ey + 1));
                        break;
                    }
                    double open = _mood == Mood.Surprised ? 1.5 : 1;
                    double h = Math.Max(0.6, 5.5 * open * (1 - lid));
                    foreach (var s in new[] { -1, 1 })
                    {
                        var c = new Point(50 + s * ex + lx, ey + ly);
                        dc.DrawEllipse(ink, null, c, 4.6 * (_mood == Mood.Surprised ? 1.3 : 1), h);
                        if (lid < 0.5) dc.DrawEllipse(Brushes.White, null, new Point(c.X + 1.3, c.Y - 1.5), 1.4, 1.4);
                    }
                    break;
                }
        }

        // ── брови ──
        if (_mood == Mood.Angry)
        {
            dc.DrawLine(thin, new Point(50 - ex - 7, ey - 9), new Point(50 - ex + 6, ey - 5));
            dc.DrawLine(thin, new Point(50 + ex + 7, ey - 9), new Point(50 + ex - 6, ey - 5));
        }
        else if (_mood == Mood.Sad)
        {
            dc.DrawLine(thin, new Point(50 - ex - 7, ey - 5), new Point(50 - ex + 6, ey - 9));
            dc.DrawLine(thin, new Point(50 + ex + 7, ey - 5), new Point(50 + ex - 6, ey - 9));
            // слеза стекает плавно
            double tear = (_time * 14) % 14;
            dc.DrawEllipse(new SolidColorBrush(C("#5AB6FF")), null, new Point(50 + ex + 4, ey + 6 + tear), 2, 3);
        }

        // ── рот ──
        double my = Kind == "Spark" ? 64 : 68;
        switch (_mood)
        {
            case Mood.Surprised: dc.DrawEllipse(ink, null, new Point(50, my + 2), 3.5, 4.5); break;
            case Mood.Laugh: dc.DrawGeometry(ink, null, Poly(true, (41, my - 2), (59, my - 2), (50, my + 8))); break;
            case Mood.Sad: dc.DrawGeometry(null, thin, Poly(false, (43, my + 4), (50, my), (57, my + 4))); break;
            case Mood.Angry: dc.DrawLine(thin, new Point(44, my + 2), new Point(56, my + 2)); break;
            default:
                if (_sleep > 0.6) dc.DrawEllipse(ink, null, new Point(50, my + 1), 2, 2.5);
                else dc.DrawGeometry(null, thin, Poly(false, (43, my - 1), (46.5, my + 3), (50, my), (53.5, my + 3), (57, my - 1)));
                break;
        }
    }

    void DrawHeart(DrawingContext dc, double cx, double cy, double s)
    {
        s *= 1 + 0.12 * Math.Sin(_time * 14);       // сердцебиение
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx, cy + s * 0.9), true, true);
            c.BezierTo(new Point(cx - s * 1.5, cy - s * 0.2), new Point(cx - s * 0.6, cy - s * 1.2), new Point(cx, cy - s * 0.3), true, true);
            c.BezierTo(new Point(cx + s * 0.6, cy - s * 1.2), new Point(cx + s * 1.5, cy - s * 0.2), new Point(cx, cy + s * 0.9), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(new SolidColorBrush(C("#FF4D6D")), null, g);
    }
}
