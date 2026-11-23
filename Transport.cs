using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Ostrovok.Views;

/// <summary>Векторные значки управления плеером в стиле Apple: скруглённые, залитые.</summary>
public static class Glyphs
{
    public const string Play = "M 7,4.5 L 19.5,12 L 7,19.5 Z";
    public const string Pause = "M 6,4.5 H 10 V 19.5 H 6 Z M 14,4.5 H 18 V 19.5 H 14 Z";
    public const string Next = "M 2.5,5.5 L 12,12 L 2.5,18.5 Z M 12,5.5 L 21.5,12 L 12,18.5 Z";
    public const string Prev = "M 21.5,5.5 L 12,12 L 21.5,18.5 Z M 12,5.5 L 2.5,12 L 12,18.5 Z";
    public const string Heart = "M 12,20 C 4,14 3,10 3,8 C 3,5.2 5.2,3.5 7.5,3.5 C 9.5,3.5 11,4.7 12,6.3 C 13,4.7 14.5,3.5 16.5,3.5 C 18.8,3.5 21,5.2 21,8 C 21,10 20,14 12,20 Z";
}

/// <summary>Круглая кнопка с векторным значком (нажатие слегка «продавливает» её).</summary>
public class RoundButton : Button
{
    readonly System.Windows.Shapes.Path _p = new()
    {
        Width = 24, Height = 24, Stretch = Stretch.None, StrokeLineJoin = PenLineJoin.Round, StrokeThickness = 2.4
    };
    string _data = "";

    public RoundButton(string data, double diameter, double glyph, bool primary, Action click)
    {
        Style = (Style)Application.Current.FindResource("RoundBtn");
        Width = Height = diameter; Margin = new Thickness(5, 0, 5, 0);
        if (primary)
        {
            SetResourceReference(BackgroundProperty, "Fg");
            _p.SetResourceReference(Shape.FillProperty, "FgInv"); _p.SetResourceReference(Shape.StrokeProperty, "FgInv");
        }
        else
        {
            // второстепенные кнопки — контурные значки без подложки, как в Atoll
            Background = Brushes.Transparent;
            _p.Fill = Brushes.Transparent; _p.StrokeThickness = 1.8;
            _p.SetResourceReference(Shape.StrokeProperty, "Fg");
            _outline = true;
        }
        Content = new Viewbox { Width = glyph, Height = glyph, Child = _p };
        Glyph = data;
        Click += (_, _) => click();
    }

    public string Glyph
    {
        get => _data;
        set { if (_data == value) return; _data = value; _p.Data = Geometry.Parse(value); }
    }

    public void SetPlaying(bool playing) => Glyph = playing ? Glyphs.Pause : Glyphs.Play;
    bool _outline;
    public void Tint(Brush b) { _p.Stroke = b; _p.Fill = _outline && !ReferenceEquals(b, Application.Current.Resources["Fg"]) ? b : (_outline ? Brushes.Transparent : b); }
}

/// <summary>Полоса перемотки как на YouTube: клик и перетаскивание ставят позицию ровно туда, куда указали.</summary>
public class SeekBar : Grid
{
    readonly Border _track = new() { Height = 4, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
    readonly Border _fill = new() { Height = 4, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
    readonly Ellipse _thumb = new() { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
    readonly ToolTip _tip = new();
    double _value;

    public bool Dragging { get; private set; }
    public Func<double, string> Format;                 // подпись времени для подсказки
    public event Action<double> Previewing;             // во время перетаскивания
    public event Action<double> Committed;              // отпустили мышь — перематываем

    public SeekBar()
    {
        Height = 22; Background = Brushes.Transparent; Cursor = Cursors.Hand; Margin = new Thickness(0, 4, 0, 0);
        _track.SetResourceReference(Border.BackgroundProperty, "TrackBg");
        _fill.SetResourceReference(Border.BackgroundProperty, "Fg");
        _thumb.Fill = Brushes.White;
        Children.Add(_track); Children.Add(_fill); Children.Add(_thumb);
        ToolTip = _tip; ToolTipService.SetInitialShowDelay(this, 0); ToolTipService.SetShowDuration(this, 60000);
        SizeChanged += (_, _) => LayoutBar();
        MouseEnter += (_, _) => Hover(true);
        MouseLeave += (_, _) => { if (!Dragging) Hover(false); };
    }

    public double Value
    {
        get => _value;
        set { if (Dragging) return; _value = Math.Clamp(value, 0, 1); LayoutBar(); }
    }

    bool _always;
    /// <summary>Всегда видимая крупная белая ручка (ползунок громкости как в Atoll).</summary>
    public bool AlwaysThumb
    {
        get => _always;
        set
        {
            _always = value;
            _thumb.Width = _thumb.Height = value ? 18 : 12;
            _thumb.Opacity = value ? 1 : 0;
            _thumb.Effect = value ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.3 } : null;
            LayoutBar();
        }
    }

    void Hover(bool on)
    {
        var d = TimeSpan.FromMilliseconds(120);
        foreach (var b in new[] { _track, _fill })
            b.BeginAnimation(HeightProperty, new DoubleAnimation(on ? 8 : 4, d) { EasingFunction = new CubicEase() });
        if (!_always) _thumb.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, d));
    }

    void LayoutBar()
    {
        double w = ActualWidth, t = _thumb.Width;
        _fill.Width = Math.Max(0, w * _value);
        _thumb.Margin = new Thickness(Math.Clamp(w * _value - t / 2, 0, Math.Max(0, w - t)), 0, 0, 0);
    }

    double Frac(MouseEventArgs e) => ActualWidth <= 0 ? 0 : Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Dragging = true; CaptureMouse();
        SetFromMouse(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        double f = Frac(e);
        if (Dragging) SetFromMouse(e);
        if (Format != null) _tip.Content = Format(f);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!Dragging) return;
        SetFromMouse(e);
        Dragging = false; ReleaseMouseCapture();
        Committed?.Invoke(_value);
        if (!IsMouseOver) Hover(false);
        e.Handled = true;
    }

    void SetFromMouse(MouseEventArgs e)
    {
        _value = Frac(e);
        LayoutBar();
        Previewing?.Invoke(_value);
    }
}
