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
    public static readonly string[] Kinds = { "Fox", "Cat", "Bear", "Ghost", "Robot", "Spark", "Slime", "Frog" };

    string _kind = "Fox";
    public string Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value; _palKind = null;
            // пиксельный призрак рисуется без сглаживания — иначе пиксели «мылятся»
            System.Windows.Media.RenderOptions.SetEdgeMode(this, value == "Ghost" ? EdgeMode.Aliased : EdgeMode.Unspecified);
        }
    }
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

    // кэши кистей, перьев и неизменных форм (чтобы не создавать их на каждом кадре)
    static readonly Dictionary<string, SolidColorBrush> Brushes_ = new();
    static Brush Br(string hex)
    {
        if (!Brushes_.TryGetValue(hex, out var b)) { b = new SolidColorBrush(C(hex)); b.Freeze(); Brushes_[hex] = b; }
        return b;
    }
    static readonly Dictionary<string, Pen> Pens_ = new();
    static Pen Pn(string hex, double w)
    {
        var k = hex + "|" + w;
        if (!Pens_.TryGetValue(k, out var p))
        {
            p = new Pen(Br(hex), w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze(); Pens_[k] = p;
        }
        return p;
    }
    static readonly Dictionary<string, Geometry> Shapes_ = new();
    static Geometry Shape(string key, Func<Geometry> make)
    {
        if (!Shapes_.TryGetValue(key, out var g)) { g = make(); if (g.CanFreeze) g.Freeze(); Shapes_[key] = g; }
        return g;
    }
    static readonly Dictionary<string, Brush> BrushShapes_ = new();
    static Brush Shape_Brush(string key, Func<Brush> make)
    {
        if (!BrushShapes_.TryGetValue(key, out var b)) { b = make(); if (b.CanFreeze) b.Freeze(); BrushShapes_[key] = b; }
        return b;
    }
    static EllipseGeometry El(double cx, double cy, double rx, double ry) => new(new Point(cx, cy), rx, ry);
    static Geometry Union(params Geometry[] gs)
    {
        Geometry r = gs[0];
        for (int i = 1; i < gs.Length; i++) r = Geometry.Combine(r, gs[i], GeometryCombineMode.Union, null);
        return r;
    }
    static readonly Typeface SymbolFont = new("Segoe UI Symbol");

    void Note(DrawingContext dc, string s, double x, double y, double size, string hex)
    {
        var ft = new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SymbolFont, size, Br(hex),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(x, y));
    }

    // ── пиксельный призрак ──
    const int GN = 32;
    const double GS = 3.125;                 // клетка в единицах 100×100
    static Geometry _gOutline, _gBody, _gShade, _gArm;

    static void BuildGhost()
    {
        if (_gBody != null) return;
        var inside = new bool[GN, GN];
        for (int y = 0; y < GN; y++)
            for (int x = 0; x < GN; x++)
            {
                double cx = x + 0.5, cy = y + 0.5;
                bool dome = cy <= 13 && Math.Pow((cx - 17) / 9.5, 2) + Math.Pow((cy - 13) / 10.5, 2) <= 1;
                double bottom = 25.5 + (((int)(cx / 3)) % 2 == 0 ? 2.5 : 0);
                bool trunk = cy > 13 && cy <= bottom && cx >= 7.5 - (cy - 13) * 0.04 && cx <= 26.5 + (cy - 13) * 0.04;
                
                inside[x, y] = dome || trunk;
            }
        bool In(int x, int y) => x >= 0 && y >= 0 && x < GN && y < GN && inside[x, y];
        bool Edge(int x, int y) => In(x, y) && (!In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1));
        bool Arm(int x, int y)
        {
            for (int i = 0; i <= 14; i++) { if (x == 18 + i / 2 && y == 17 + i / 2) return In(x, y); }
            return (x >= 24 && x <= 26 && y >= 24 && y <= 25) && In(x, y);
        }
        Geometry Make(Func<int, int, bool> p)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
                for (int y = 0; y < GN; y++)
                {
                    int x = 0;
                    while (x < GN)
                    {
                        if (!p(x, y)) { x++; continue; }
                        int x0 = x;
                        while (x < GN && p(x, y)) x++;
                        c.BeginFigure(new Point(x0, y), true, true);
                        c.LineTo(new Point(x, y), false, false); c.LineTo(new Point(x, y + 1), false, false); c.LineTo(new Point(x0, y + 1), false, false);
                    }
                }
            g.Freeze();
            return g;
        }
        _gOutline = Make((x, y) => Edge(x, y));
        _gBody = Make((x, y) => In(x, y) && !Edge(x, y));
        _gShade = Make((x, y) => In(x, y) && !Edge(x, y) && (x + 0.5 >= 22.5 || y + 0.5 >= 24));
        _gArm = Make((x, y) => false);
    }

    void EnsurePalette()
    {
        if (_palKind == Kind) return;
        _palKind = Kind;
        var (body, dark) = Kind switch
        {
            "Cat" => (C("#FFFFFF"), C("#1B1B1B")),
            "Bear" => (C("#A97D66"), C("#4E2E22")),
            "Ghost" => (C("#FFFFFF"), C("#17171B")),
            "Robot" => (C("#8FD3FF"), C("#2F6E99")),
            "Spark" => (C("#E8825B"), C("#A8482A")),
            "Slime" => (C("#7EDC68"), C("#1E5C2B")),
            "Frog" => (C("#4DB848"), C("#173F20")),
            "Snowman" => (C("#F4F8FF"), C("#5E7096")),
            "Santa" => (C("#FFD3B3"), C("#8C3A2B")),
            "Bullfinch" => (C("#EE6A55"), C("#6B2218")),
            "Gingerbread" => (C("#C98A4B"), C("#5A3517")),
            "Reindeer" => (C("#B07A45"), C("#4B2C14")),
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
        DrawFront(dc);

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
                {
                    // белый котик-«каракуля» в наушниках
                    var shape = Shape("cat.body", () => Union(El(50, 50, 30, 23),
                        Geometry.Parse("M 22,46 L 25,18 L 45,30 Z"), Geometry.Parse("M 78,46 L 75,18 L 55,30 Z"),
                        El(50, 79, 17, 12), El(41, 91, 6.5, 3.8), El(59, 91, 6.5, 3.8)));
                    dc.DrawGeometry(_body, Pn("#1B1B1B", 2.6), shape);
                    var gray = Pn("#8F8F96", 1.9);
                    dc.DrawLine(gray, new Point(44, 34), new Point(46.5, 41));
                    dc.DrawLine(gray, new Point(49.5, 33), new Point(52, 40));
                    dc.DrawLine(gray, new Point(55, 34), new Point(57.5, 41));
                    dc.DrawLine(gray, new Point(52, 74), new Point(58, 81));           // серый крестик на животе
                    dc.DrawLine(gray, new Point(58, 74), new Point(52, 81));
                    dc.DrawLine(Pn("#1B1B1B", 2.4), new Point(66, 75), new Point(77, 68));   // лапка
                    dc.DrawEllipse(_body, Pn("#1B1B1B", 2.2), new Point(78.5, 67), 2.8, 2.8);
                    dc.DrawGeometry(null, Pn("#9A9AA2", 1.8), Shape("cat.cable", () => Geometry.Parse("M 33,64 Q 28,74 36,79")));
                    return;
                }
            case "Bear":
                {
                    // мохнатый мишка: контур дрожит, поэтому шерсть «шевелится»
                    string ink = "#4E2E22";
                    var shapes = new Geometry[]
                    {
                        Wobble(33, 21, 9.5, 9.5, 18, 0.6), Wobble(67, 21, 9.5, 9.5, 18, 0.6),
                        Wobble(31, 87, 9.5, 6.5, 16, 0.5), Wobble(67, 87, 9.5, 6.5, 16, 0.5),
                        Wobble(23, 58, 8, 9, 18, 0.5), Wobble(77, 58, 8, 9, 18, 0.5),
                        Wobble(50, 72, 27, 18, 32, 0.8),
                        Wobble(50, 38, 26, 22, 36, 0.8),
                    };
                    foreach (var s in shapes) dc.DrawGeometry(null, Pn(ink, 5.4), s);
                    foreach (var s in shapes) dc.DrawGeometry(_body, null, s);
                    dc.DrawEllipse(Br("#8B5D4E"), null, new Point(33.5, 22.5), 5, 5);
                    dc.DrawEllipse(Br("#8B5D4E"), null, new Point(66.5, 22.5), 5, 5);
                    dc.DrawEllipse(Br("#E2B793"), null, new Point(50, 74), 17, 9.5);          // пузико
                    dc.DrawEllipse(Br("#E2B793"), null, new Point(21.5, 60), 4.4, 4.8);       // подушечки лап
                    dc.DrawEllipse(Br("#E2B793"), null, new Point(78.5, 60), 4.4, 4.8);
                    dc.DrawEllipse(Br("#E2B793"), null, new Point(30, 89), 4.2, 3);
                    dc.DrawEllipse(Br("#E2B793"), null, new Point(68, 89), 4.2, 3);
                    dc.DrawEllipse(Br("#8F6252"), null, new Point(50, 46.5), 9.5, 6.4);       // мордочка
                    dc.DrawEllipse(Br("#33201A"), null, new Point(50, 43.6), 3.4, 2.4);       // носик
                    dc.DrawGeometry(null, Pn(ink, 1.5), Shape("bear.curl", () => Geometry.Parse("M 45,29.5 C 46.5,25.5 49,26 50,29 C 51,31.5 53.5,31 54.5,27.5")));
                    return;
                }
            case "Robot":
                dc.DrawLine(_pen, new Point(50, 20), new Point(50, 8));
                dc.DrawEllipse(new SolidColorBrush(C("#FF6B6B")), _pen, new Point(50, 7), 3.5, 3.5);
                dc.DrawGeometry(_body, _pen, Poly(true, (14, 30), (86, 30), (86, 88), (14, 88)));
                return;
            case "Frog":
                {
                    // пухлая лягушка с цветком: единый контур тела, светлое пузико, розовые щёчки
                    var body = Shape("frog.body", () => Union(El(51, 52, 35, 31), El(30, 25, 14.5, 12), El(72, 25, 14.5, 12), El(42, 84, 8, 5.5), El(59, 84, 8, 5.5)));
                    dc.DrawGeometry(_body, Pn("#173F20", 3.2), body);
                    dc.DrawEllipse(Br("#A9D653"), null, new Point(51, 56), 27, 20);
                    dc.DrawEllipse(Br("#D9F0909C"), null, new Point(26, 33), 6.5, 4.5);
                    dc.DrawEllipse(Br("#D9F0909C"), null, new Point(76, 32), 6.5, 4.5);
                    return;
                }
            case "Reindeer":
                {
                    var antler = new Pen(_ink, 3.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    foreach (var s in new[] { -1, 1 })
                    {
                        double X(double x) => 50 + s * x;
                        dc.DrawLine(antler, new Point(X(20), 36), new Point(X(30), 10));
                        dc.DrawLine(antler, new Point(X(30), 10), new Point(X(22), 4));
                        dc.DrawLine(antler, new Point(X(26), 22), new Point(X(38), 18));
                        dc.DrawGeometry(_body, _pen, Poly(true, (50 + s * 33, 46), (50 + s * 47, 32), (50 + s * 24, 34)));
                    }
                    break;
                }
            case "Gingerbread":
                dc.DrawGeometry(_body, _pen, Wobble(50, 58, 36, 31));
                dc.DrawEllipse(null, new Pen(Brushes.White, 2.2) { DashStyle = new DashStyle(new double[] { 1.2, 1.8 }, 0), StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    new Point(50, 58), 31, 26);
                return;
            case "Santa":
                dc.DrawGeometry(_body, _pen, Wobble(50, 58, 36, 31));
                dc.DrawGeometry(Brushes.White, _pen, Wobble(50, 83, 30, 15, 18, 0.7));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 90, 120)), null, new Point(26, 62), 6, 4);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 90, 120)), null, new Point(74, 62), 6, 4);
                return;
            case "Slime":
                {
                    // глянцевый слайм: полупрозрачный градиент, светящиеся глаза, капли и лужица
                    double sq = Math.Sin(_time * 3) * 2;
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(6, 88), true, true);
                        c.BezierTo(new Point(6, 70), new Point(18, 55), new Point(30, 38 + sq), true, true);
                        c.BezierTo(new Point(33, 32 + sq), new Point(33, 26 + sq), new Point(36, 19 + sq), true, true);
                        c.BezierTo(new Point(40, 24 + sq), new Point(46, 26 + sq), new Point(52, 26 + sq), true, true);
                        c.BezierTo(new Point(80, 26 + sq), new Point(94, 50), new Point(94, 70), true, true);
                        c.BezierTo(new Point(94, 82), new Point(90, 90), new Point(82, 90), true, true);
                        c.BezierTo(new Point(70, 96 + sq * 0.4), new Point(30, 96 + sq * 0.4), new Point(18, 92), true, true);
                        c.BezierTo(new Point(12, 91), new Point(6, 90), new Point(6, 88), true, true);
                    }
                    g.Freeze();
                    var jelly = (Brush)Shape_Brush("slime.jelly", () =>
                    {
                        var rb = new RadialGradientBrush { Center = new Point(0.38, 0.32), GradientOrigin = new Point(0.3, 0.25), RadiusX = 0.85, RadiusY = 0.85 };
                        rb.GradientStops.Add(new GradientStop(C("#C8F59B"), 0)); rb.GradientStops.Add(new GradientStop(C("#86DF6C"), 0.45)); rb.GradientStops.Add(new GradientStop(C("#3DA13F"), 1));
                        return rb;
                    });
                    dc.DrawEllipse(Br("#7F6FCF5C"), Pn("#1E5C2B", 1.2), new Point(7, 96.5), 5, 2);                  // лужица
                    dc.DrawGeometry(jelly, Pn("#1E5C2B", 1.8), g);
                    dc.DrawEllipse(Br("#26FFFFFF"), null, new Point(56, 76), 26, 12);                                 // свет внутри
                    dc.DrawGeometry(Br("#9AB8F08A"), Pn("#2F8A3A", 0.9), Shape("slime.drip1", () => Geometry.Parse("M 20,50 C 17,57 17,63 20,65 C 23,63 23,57 20,50 Z")));
                    dc.DrawGeometry(Br("#9AB8F08A"), Pn("#2F8A3A", 0.9), Shape("slime.drip2", () => Geometry.Parse("M 28,66 C 25.5,75 25.5,82 29,84 C 32.5,82 32.5,75 30,66 Z")));
                    dc.PushTransform(new RotateTransform(-35, 24, 44 + sq));
                    dc.DrawEllipse(Br("#99FFFFFF"), null, new Point(24, 44 + sq), 9, 4.2);                            // глянцевый блик
                    dc.Pop();
                    dc.DrawEllipse(Br("#99FFFFFF"), null, new Point(15, 60), 2, 2);
                    return;
                }
            case "Ghost":
                {
                    BuildGhost();
                    dc.PushTransform(new TranslateTransform(3, 0));
                    dc.PushTransform(new ScaleTransform(GS, GS));
                    dc.DrawGeometry(Br("#6E6E78"), null, _gOutline);
                    dc.DrawGeometry(Brushes.White, null, _gBody);
                    dc.DrawGeometry(Br("#D4D4DC"), null, _gShade);
                    
                    if (_mood == Mood.Normal)
                    {
                        // пиксельные грустные глазки с «треугольным» взглядом; следят за курсором, моргают
                        int dx = (int)Math.Round(_lx), dy = (int)Math.Round(_ly * 0.8);
                        double blink = _blinkT >= 0 ? Math.Sin(Math.PI * _blinkT / 0.16) : 0;
                        bool shut = Math.Max(blink, _sleep) > 0.5;
                        var k = Br("#17171B");
                        void Px(int x, int y) => dc.DrawRectangle(k, null, new Rect(x + dx, y + dy, 1, 1));
                        if (shut)
                        {
                            for (int i = 0; i < 3; i++) { Px(11 + i, 12); Px(19 + i, 12); }
                        }
                        else
                        {
                            foreach (var (x, y) in new[] { (2, 0), (1, 1), (2, 1), (0, 2), (1, 2), (2, 2) }) Px(11 + x, 10 + y);
                            foreach (var (x, y) in new[] { (0, 0), (1, 1), (0, 1), (2, 2), (1, 2), (0, 2) }) Px(19 + x, 10 + y);
                        }
                        Px(15, 15); Px(16, 15);
                    }
                    dc.Pop(); dc.Pop();
                    return;
                }
        }
        dc.DrawGeometry(_body, _pen, Wobble(50, 58, 36, 31));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 90, 120)), null, new Point(26, 66), 6, 4);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 90, 120)), null, new Point(74, 66), 6, 4);
    }

    // детали поверх лица: шапки, носы, шарфы
    void DrawFront(DrawingContext dc)
    {
        var red = new SolidColorBrush(Color.FromRgb(229, 57, 59));
        switch (Kind)
        {
            case "Snowman":
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(255, 138, 43)), _thin, Poly(true, (45, 60), (66, 63), (45, 66)));
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(43, 47, 58)), _pen, Poly(true, (32, 30), (36, 6), (64, 6), (68, 30)));
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(217, 75, 75)), null, Poly(true, (33.5, 24), (66.5, 24), (66.8, 18), (33.2, 18)));
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(43, 47, 58)), _pen, Poly(true, (24, 31), (76, 31), (76, 36), (24, 36)));
                dc.DrawGeometry(red, _pen, Poly(true, (22, 82), (78, 82), (76, 92), (24, 92)));
                dc.DrawGeometry(red, _pen, Poly(true, (60, 90), (73, 90), (71, 100), (58, 100)));
                break;
            case "Santa":
                dc.DrawGeometry(red, _pen, Poly(true, (18, 40), (30, 14), (58, 4), (80, 26), (84, 40)));
                dc.DrawGeometry(Brushes.White, _pen, Poly(true, (14, 36), (86, 36), (86, 46), (14, 46)));
                dc.DrawEllipse(Brushes.White, _pen, new Point(60, 5), 7, 7);
                break;
            case "Bullfinch":
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(42, 42, 51)), _pen, Poly(true, (16, 44), (24, 28), (50, 21), (76, 28), (84, 44), (70, 40), (50, 37), (30, 40)));
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(42, 42, 51)), _thin, Poly(true, (44, 59), (56, 59), (50, 67)));
                break;
            case "Gingerbread":
                dc.DrawGeometry(red, _thin, Poly(true, (50, 88), (35, 80), (35, 96)));
                dc.DrawGeometry(red, _thin, Poly(true, (50, 88), (65, 80), (65, 96)));
                dc.DrawEllipse(Brushes.White, _thin, new Point(50, 88), 3.2, 3.2);
                break;
            case "Frog":
                {
                    // лягушка обнимает цветок: стебель, листик, лапки и лепестки
                    double sw = Math.Sin(_time * 2.2) * 1.6;
                    string ink = "#173F20";
                    dc.DrawGeometry(null, Pn("#2E7D3B", 2.8), Shape("frog.stem", () => Geometry.Parse("M 57,61 Q 55,70 49,78")));
                    dc.DrawGeometry(Br("#7BC47A"), Pn(ink, 1.6), Shape("frog.leaf", () => Geometry.Parse("M 53,69 C 42,67 38,56 42,49 C 50,53 56,60 53,69 Z")));
                    dc.PushTransform(new TranslateTransform(sw * 0.5, 0));
                    dc.DrawGeometry(Brushes.White, Pn(ink, 2.1), Shape("frog.flower", () =>
                    {
                        var parts = new List<Geometry> { El(58, 51, 5.5, 5.5) };
                        for (int i = 0; i < 5; i++)
                        {
                            double a = (-90 + i * 72) * Math.PI / 180;
                            parts.Add(El(58 + Math.Cos(a) * 7, 51 + Math.Sin(a) * 7, 6.2, 6.2));
                        }
                        return Union(parts.ToArray());
                    }));
                    dc.DrawEllipse(Br("#F6D44B"), Pn(ink, 1.1), new Point(58.5, 52), 2.8, 2.4);
                    dc.Pop();
                    dc.DrawGeometry(null, Pn(ink, 2.4), Shape("frog.armL", () => Geometry.Parse("M 31,67 Q 42,73 54,71")));
                    dc.DrawGeometry(null, Pn(ink, 2.4), Shape("frog.armR", () => Geometry.Parse("M 74,67 Q 64,73 54,71")));
                    break;
                }
            case "Cat":
                {
                    // наушники: дужка и чашки; рядом пляшут ноты
                    dc.DrawGeometry(null, Pn("#1B1B1B", 4.8), Shape("cat.band", () => Geometry.Parse("M 19,50 A 31,33 0 0 1 81,50")));
                    foreach (double x in new[] { 18.0, 82.0 })
                        dc.DrawEllipse(Br("#1B1B1B"), Pn("#8A8A92", 1.3), new Point(x, 53), 6.5, 10.5);
                    double nb = Math.Sin(_time * 3) * 2.5 * (1 + _dance);
                    Note(dc, "♫", 0, 36 + nb, 15, "#8F8F96");
                    Note(dc, "♪", 85, 18 - nb, 17, "#8F8F96");
                    break;
                }
            case "Reindeer":
                dc.DrawEllipse(red, _thin, new Point(50, 63), 6, 5);
                dc.DrawEllipse(Brushes.White, null, new Point(48.5, 61.5), 1.4, 1.2);
                break;
        }
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

    struct Face { public double Cx, Ex, Ey, My, Er, Mw; public bool Glow, Arc; }

    Face Spec() => Kind switch
    {
        "Spark" => new Face { Cx = 50, Ex = 6, Ey = 53, My = 64, Er = 4.6, Mw = 7 },
        "Robot" => new Face { Cx = 50, Ex = 13, Ey = 55, My = 68, Er = 4.6, Mw = 7 },
        "Frog" => new Face { Cx = 51, Ex = 21, Ey = 25.5, My = 31, Er = 5.2, Mw = 4.5 },
        "Cat" => new Face { Cx = 50, Ex = 11, Ey = 52, My = 57, Er = 2.5, Mw = 4.5 },
        "Bear" => new Face { Cx = 50, Ex = 8.5, Ey = 43, My = 51.5, Er = 2.7, Mw = 3.2 },
        "Slime" => new Face { Cx = 66, Ex = 10, Ey = 44, My = 57, Er = 6.8, Mw = 5, Glow = true, Arc = true },
        "Ghost" => new Face { Cx = 54.7, Ex = 12.5, Ey = 36, My = 47, Er = 3.4, Mw = 5 },
        _ => new Face { Cx = 50, Ex = 13, Ey = 54, My = 68, Er = 4.6, Mw = 7 },
    };

    void DrawFace(DrawingContext dc)
    {
        if (Kind == "Ghost" && _mood == Mood.Normal) return;       // у пиксельного призрака лицо нарисовано вместе с телом
        var f = Spec();
        double cx = f.Cx, ex = f.Ex, ey = f.Ey;
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
                foreach (var s in new[] { -1, 1 }) DrawHeart(dc, cx + s * ex, ey, Math.Max(5, f.Er * 1.5));
                break;
            case Mood.Laugh:
                foreach (var s in new[] { -1, 1 })
                    dc.DrawGeometry(null, thin, Poly(false, (cx + s * ex - 5, ey + 3), (cx + s * ex, ey - 3), (cx + s * ex + 5, ey + 3)));
                break;
            case Mood.Cool:
                dc.DrawGeometry(ink, null, Poly(true, (cx - ex - 9, ey - 5), (cx + ex + 9, ey - 5), (cx + ex + 7, ey + 5), (cx + ex - 2, ey + 5),
                    (cx, ey + 1), (cx - ex + 2, ey + 5), (cx - ex - 7, ey + 5)));
                break;
            case Mood.Dizzy:
                foreach (var s in new[] { -1, 1 })
                {
                    double ecx = cx + s * ex; double ang = _time * 10;
                    var pts = new List<(double, double)>();
                    for (int i = 0; i < 14; i++)
                    {
                        double a = ang + i * 0.8; double r = 1 + i * 0.4;
                        pts.Add((ecx + Math.Cos(a) * r, ey + Math.Sin(a) * r));
                    }
                    dc.DrawGeometry(null, thin, Poly(false, pts.ToArray()));
                }
                break;
            default:
                {
                    if (_sleep > 0.85)
                    {
                        foreach (var s in new[] { -1, 1 })
                            dc.DrawLine(thin, new Point(cx + s * ex - f.Er, ey + 1), new Point(cx + s * ex + f.Er, ey + 1));
                        break;
                    }
                    double open = _mood == Mood.Surprised ? 1.5 : 1;
                    double rx = f.Er * (_mood == Mood.Surprised ? 1.3 : 1);
                    double h = Math.Max(0.5, f.Er * 1.2 * open * (1 - lid));
                    foreach (var s in new[] { -1, 1 })
                    {
                        var c = new Point(cx + s * ex + lx * (f.Er / 4.6 + 0.3), ey + ly * (f.Er / 4.6 + 0.3));
                        if (f.Glow)
                        {
                            // светящиеся глаза слайма
                            dc.DrawEllipse(Br("#40FFF6B0"), null, c, rx * 1.35, Math.Max(0.6, h * 1.35));
                            dc.DrawEllipse(Shape_Brush("slime.eye", () =>
                            {
                                var rb = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.55, RadiusY = 0.55 };
                                rb.GradientStops.Add(new GradientStop(C("#FFFFFFFF"), 0)); rb.GradientStops.Add(new GradientStop(C("#FFFFF3A6"), 0.7)); rb.GradientStops.Add(new GradientStop(C("#FFEFE27A"), 1));
                                return rb;
                            }), null, c, rx, h);
                        }
                        else
                        {
                            dc.DrawEllipse(ink, null, c, rx, h);
                            if (lid < 0.5 && f.Er >= 3.5) dc.DrawEllipse(Brushes.White, null, new Point(c.X + 1.3, c.Y - 1.5), 1.4, 1.4);
                            else if (lid < 0.5 && f.Er >= 2.5) dc.DrawEllipse(Brushes.White, null, new Point(c.X + 0.8, c.Y - 0.9), 0.8, 0.8);
                        }
                    }
                    break;
                }
        }

        // ── брови ──
        if (_mood == Mood.Angry)
        {
            dc.DrawLine(thin, new Point(cx - ex - 7, ey - 9), new Point(cx - ex + 6, ey - 5));
            dc.DrawLine(thin, new Point(cx + ex + 7, ey - 9), new Point(cx + ex - 6, ey - 5));
        }
        else if (_mood == Mood.Sad)
        {
            dc.DrawLine(thin, new Point(cx - ex - 7, ey - 5), new Point(cx - ex + 6, ey - 9));
            dc.DrawLine(thin, new Point(cx + ex + 7, ey - 5), new Point(cx + ex - 6, ey - 9));
            // слеза стекает плавно
            double tear = (_time * 14) % 14;
            dc.DrawEllipse(new SolidColorBrush(C("#5AB6FF")), null, new Point(cx + ex + 4, ey + 6 + tear), 2, 3);
        }

        // ── рот ──
        double my = f.My, mw = f.Mw, ms = Math.Max(0.6, mw / 7);
        switch (_mood)
        {
            case Mood.Surprised: dc.DrawEllipse(ink, null, new Point(cx, my + 2), 3.5 * ms, 4.5 * ms); break;
            case Mood.Laugh: dc.DrawGeometry(ink, null, Poly(true, (cx - 9 * ms, my - 2), (cx + 9 * ms, my - 2), (cx, my + 8 * ms))); break;
            case Mood.Sad: dc.DrawGeometry(null, thin, Poly(false, (cx - 7 * ms, my + 4), (cx, my), (cx + 7 * ms, my + 4))); break;
            case Mood.Angry: dc.DrawLine(thin, new Point(cx - 6 * ms, my + 2), new Point(cx + 6 * ms, my + 2)); break;
            default:
                if (_sleep > 0.6) dc.DrawEllipse(ink, null, new Point(cx, my + 1), 2 * ms, 2.5 * ms);
                else if (f.Arc) dc.DrawGeometry(null, thin, Poly(false, (cx - mw, my), (cx - mw * 0.4, my + 2.6), (cx + mw * 0.4, my + 2.6), (cx + mw, my)));
                else dc.DrawGeometry(null, thin, Poly(false, (cx - mw, my - 1), (cx - mw / 2, my + 3), (cx, my), (cx + mw / 2, my + 3), (cx + mw, my - 1)));
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
