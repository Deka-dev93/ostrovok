using System.Windows;
using System.Windows.Media;

namespace Ostrovok.Views;

/// <summary>Эквалайзер из четырёх столбиков для пилюли.</summary>
public class Equalizer : FrameworkElement
{
    public bool Active { get; set; }
    readonly DateTime _t0 = DateTime.Now;

    public Equalizer() { Width = 22; Height = 18; }

    protected override void OnRender(DrawingContext dc)
    {
        double t = (DateTime.Now - _t0).TotalSeconds;
        var brush = (Brush)Application.Current.Resources["Accent"];
        for (int i = 0; i < 4; i++)
        {
            double h = Active ? 4 + 12 * (0.5 + 0.5 * Math.Sin(t * (5 + i * 1.7) + i * 1.3)) : 3;
            dc.DrawRoundedRectangle(brush, null, new Rect(i * 5.5, (ActualHeight - h) / 2, 3, h), 1.5, 1.5);
        }
    }
}

/// <summary>Кольцо прогресса (таймер / помодоро).</summary>
public class Ring : FrameworkElement
{
    public double Progress { get; set; }
    public bool Indeterminate { get; set; }

    public Ring() { Width = 22; Height = 22; }

    protected override void OnRender(DrawingContext dc)
    {
        double r = Math.Min(ActualWidth, ActualHeight) / 2 - 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var back = (Brush)Application.Current.Resources["Card"];
        dc.DrawEllipse(null, new Pen(back, 3), c, r, r);
        double p = Math.Clamp(Progress, 0, 0.9999);
        if (p <= 0) return;
        double a = -Math.PI / 2 + p * Math.PI * 2;
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(new Point(c.X, c.Y - r), false, false);
            s.ArcTo(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)), new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, new Pen((Brush)Application.Current.Resources["Accent"], 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
    }
}
