using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ostrovok.Views;

public enum Mood { Normal, Love, Angry, Surprised, Sad, Dizzy, Laugh, Cool }

/// <summary>
/// Маленький персонаж, который живёт в пилюле. Рисуется «от руки» с частотой 12 кадров/с
/// с дрожащим чернильным контуром, следит глазами за курсором, моргает, танцует, спит и показывает эмоции.
/// </summary>
public class CharacterControl : FrameworkElement
{
    public static readonly string[] Kinds = { "Fox", "Cat", "Bear", "Ghost", "Robot", "Spark" };

    public string Kind { get; set; } = "Fox";
    public bool Dancing { get; set; }
    public bool Sleeping { get; set; }
    public bool Thinking { get; set; }
    public double LookX { get; set; }
    public double LookY { get; set; }

    Mood _mood = Mood.Normal;
    DateTime _moodUntil;
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 12) };
    readonly Random _rnd = new();
    int _frame;
    double _blinkAt = 3;   // секунды до следующего моргания
    double _blink;         // 0..1, закрытость глаз
    DateTime _t0 = DateTime.Now;

    static readonly Mood[] Moods = { Mood.Love, Mood.Angry, Mood.Surprised, Mood.Sad, Mood.Dizzy, Mood.Laugh, Mood.Cool };

    public CharacterControl()
    {
        IsHitTestVisible = true;
        Cursor = System.Windows.Input.Cursors.Hand;
        Loaded += (_, _) => _t.Start();
        Unloaded += (_, _) => _t.Stop();
        _t.Tick += (_, _) => { _frame++; Update(); if (IsVisible) InvalidateVisual(); };
        MouseLeftButtonUp += (_, _) => ShowEmotion(Moods[_rnd.Next(Moods.Length)]);
    }

    public void ShowEmotion(Mood m, double seconds = 2.2) { _mood = m; _moodUntil = DateTime.Now.AddSeconds(seconds); }

    void Update()
    {
        if (_mood != Mood.Normal && DateTime.Now > _moodUntil) _mood = Mood.Normal;
        double dt = 1.0 / 12;
        _blinkAt -= dt;
        if (_blinkAt <= 0) { _blink = 1; _blinkAt = 2 + _rnd.NextDouble() * 4; }
        else _blink = Math.Max(0, _blink - dt * 7);
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    (Color body, Color dark) Palette() => Kind switch
    {
        "Cat" => (C("#C9CED6"), C("#6B7280")),
        "Bear" => (C("#C79A6B"), C("#6B4A2B")),
        "Ghost" => (C("#F1F3FF"), C("#8E96C9")),
        "Robot" => (C("#8FD3FF"), C("#2F6E99")),
        "Spark" => (C("#E8825B"), C("#A8482A")),
        _ => (C("#FF9B4A"), C("#8F4A12")),
    };

    // дрожащий овал / многоугольник
    StreamGeometry Wobble(double cx, double cy, double rx, double ry, int n = 26, double amp = 0.9)
    {
        var g = new StreamGeometry();
        var rnd = new Random(_frame * 31 + (int)(cx * 7 + cy * 13 + rx));
        using var c = g.Open();
        for (int i = 0; i < n; i++)
        {
            double a = i * Math.PI * 2 / n;
            var p = new Point(cx + Math.Cos(a) * rx + (rnd.NextDouble() - .5) * amp * 2,
                              cy + Math.Sin(a) * ry + (rnd.NextDouble() - .5) * amp * 2);
            if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
        }
        g.Freeze();
        return g;
    }

    StreamGeometry Poly(bool closed, params (double x, double y)[] pts)
    {
        var g = new StreamGeometry();
        var rnd = new Random(_frame * 17 + pts.Length);
        using var c = g.Open();
        for (int i = 0; i < pts.Length; i++)
        {
            var p = new Point(pts[i].x + (rnd.NextDouble() - .5) * 1.4, pts[i].y + (rnd.NextDouble() - .5) * 1.4);
            if (i == 0) c.BeginFigure(p, closed, closed); else c.LineTo(p, true, true);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side < 4) return;
        double secs = (DateTime.Now - _t0).TotalSeconds;
        var (body, dark) = Palette();
        var bodyBrush = new SolidColorBrush(body); var inkBrush = new SolidColorBrush(dark);
        var pen = new Pen(inkBrush, 2.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        dc.PushTransform(new TranslateTransform((ActualWidth - side) / 2, (ActualHeight - side) / 2));
        dc.PushTransform(new ScaleTransform(side / 100, side / 100));

        // движение: танец / дыхание
        double bob = Dancing ? Math.Sin(secs * 9) * 5 : Math.Sin(secs * 2) * 1.2;
        double tilt = Dancing ? Math.Sin(secs * 4.5) * 9 : (Thinking ? -6 : 0);
        if (_mood == Mood.Laugh) bob += Math.Sin(secs * 30) * 1.5;
        dc.PushTransform(new TranslateTransform(0, bob));
        dc.PushTransform(new RotateTransform(tilt, 50, 85));

        if (Kind == "Spark") DrawSpark(dc, secs, bodyBrush, pen);
        else DrawHead(dc, bodyBrush, inkBrush, pen);

        DrawFace(dc, inkBrush, pen);

        if (Sleeping)
        {
            var ft = new FormattedText("z", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 18, inkBrush, 1);
            dc.DrawText(ft, new Point(76, 6 + Math.Sin(secs * 2) * 3));
        }
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }

    void DrawHead(DrawingContext dc, Brush body, Brush ink, Pen pen)
    {
        switch (Kind)
        {
            case "Fox":
                dc.DrawGeometry(body, pen, Poly(true, (17, 44), (22, 8), (44, 28)));
                dc.DrawGeometry(body, pen, Poly(true, (83, 44), (78, 8), (56, 28)));
                dc.DrawGeometry(Brushes.White, null, Poly(true, (30, 22), (26, 16), (38, 25)));
                break;
            case "Cat":
                dc.DrawGeometry(body, pen, Poly(true, (16, 46), (20, 10), (44, 28)));
                dc.DrawGeometry(body, pen, Poly(true, (84, 46), (80, 10), (56, 28)));
                break;
            case "Bear":
                dc.DrawGeometry(body, pen, Wobble(22, 28, 11, 11, 14));
                dc.DrawGeometry(body, pen, Wobble(78, 28, 11, 11, 14));
                break;
            case "Robot":
                dc.DrawLine(pen, new Point(50, 20), new Point(50, 8));
                dc.DrawEllipse(new SolidColorBrush(C("#FF6B6B")), pen, new Point(50, 7), 3.5, 3.5);
                dc.DrawGeometry(body, pen, Poly(true, (14, 30), (86, 30), (86, 88), (14, 88)));
                return;
            case "Ghost":
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(14, 90), true, true);
                        c.LineTo(new Point(14, 52), true, true);
                        c.ArcTo(new Point(86, 52), new Size(36, 38), 0, false, SweepDirection.Clockwise, true, true);
                        c.LineTo(new Point(86, 90), true, true);
                        for (int i = 0; i < 4; i++)
                            c.ArcTo(new Point(86 - (i + 1) * 18, 90), new Size(9, 7), 0, false,
                                i % 2 == 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, true);
                    }
                    g.Freeze();
                    dc.DrawGeometry(body, pen, g);
                    return;
                }
        }
        dc.DrawGeometry(body, pen, Wobble(50, 58, 36, 31));
        // щёчки
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 90, 120)), null, new Point(26, 66), 6, 4);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 90, 120)), null, new Point(74, 66), 6, 4);
    }

    // Claude: оранжевая «искра» — лучи волной вытягиваются и втягиваются
    void DrawSpark(DrawingContext dc, double secs, Brush body, Pen pen)
    {
        var rayPen = new Pen(body, 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        const int n = 12;
        for (int i = 0; i < n; i++)
        {
            double a = i * Math.PI * 2 / n;
            double len = 26 + 10 * Math.Sin(secs * 3 + i * 0.9) + (Dancing ? 5 * Math.Sin(secs * 9 + i) : 0);
            dc.DrawLine(rayPen, new Point(50 + Math.Cos(a) * 10, 55 + Math.Sin(a) * 10),
                                new Point(50 + Math.Cos(a) * len, 55 + Math.Sin(a) * len));
        }
        dc.DrawEllipse(body, null, new Point(50, 55), 14, 14);
    }

    void DrawFace(DrawingContext dc, Brush ink, Pen pen)
    {
        double ey = Kind == "Spark" ? 53 : (Kind == "Robot" ? 55 : 54);
        double ex = Kind == "Spark" ? 6 : 13;
        double lx = LookX * 3, ly = LookY * 2.2;
        var thin = new Pen(ink, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        Brush white = Brushes.White;

        // ── глаза ──
        if (Sleeping)
        {
            foreach (var s in new[] { -1, 1 })
                dc.DrawLine(thin, new Point(50 + s * ex - 5, ey + 1), new Point(50 + s * ex + 5, ey + 1));
        }
        else switch (_mood)
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
                    double cx = 50 + s * ex; double ang = _frame * 0.8;
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
                    double open = _mood == Mood.Surprised ? 1.5 : 1;
                    double h = Math.Max(0.5, 5.5 * open * (1 - _blink));
                    foreach (var s in new[] { -1, 1 })
                    {
                        var c = new Point(50 + s * ex + lx, ey + ly);
                        dc.DrawEllipse(ink, null, c, 4.6 * (_mood == Mood.Surprised ? 1.3 : 1), h);
                        if (_blink < 0.5) dc.DrawEllipse(white, null, new Point(c.X + 1.3, c.Y - 1.5), 1.4, 1.4);
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
            dc.DrawEllipse(new SolidColorBrush(C("#5AB6FF")), null, new Point(50 + ex + 4, ey + 9 + (_frame % 12)), 2, 3);
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
                if (Sleeping) dc.DrawEllipse(ink, null, new Point(50, my + 1), 2, 2.5);
                else dc.DrawGeometry(null, thin, Poly(false, (43, my - 1), (46.5, my + 3), (50, my), (53.5, my + 3), (57, my - 1)));
                break;
        }
    }

    void DrawHeart(DrawingContext dc, double cx, double cy, double s)
    {
        double beat = 1 + 0.12 * Math.Sin(_frame * 1.2);
        s *= beat;
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
