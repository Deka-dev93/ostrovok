using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Ostrovok.Services;

namespace Ostrovok.Views;

public enum ShotMode { Region, Ocr }

/// <summary>Затемнённый оверлей на весь виртуальный экран: выделяете область мышью.</summary>
public class ScreenshotWindow : Window
{
    readonly BitmapSource _full;
    readonly ShotMode _mode;
    readonly Canvas _canvas = new();
    readonly Rectangle _sel = new() { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = Brushes.Transparent };
    readonly Rectangle _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)) };
    readonly Image _img = new() { Stretch = Stretch.Fill };
    Point _a; bool _drag;
    static bool _busy;

    public static void StartRegion() => Start(ShotMode.Region);
    public static void StartOcr() => Start(ShotMode.Ocr);

    public static void StartFull()
    {
        var img = ScreenshotService.CaptureFullScreen();
        if (img != null) new EditorWindow(img).Show();
    }

    static void Start(ShotMode m)
    {
        if (_busy) return;
        _busy = true;
        // остров не должен попасть в кадр: прячем, ждём перерисовки, снимаем
        App.Island?.CloseIsland();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(350);
            var shot = ScreenshotService.CaptureFullScreen();
            if (shot == null) { _busy = false; return; }
            new ScreenshotWindow(shot, m).Show();
        });
    }

    ScreenshotWindow(BitmapSource full, ShotMode mode)
    {
        _full = full; _mode = mode;
        WindowStyle = WindowStyle.None; AllowsTransparency = false; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross; Background = Brushes.Black;
        _img.Source = full;

        var root = new Grid();
        root.Children.Add(_img);
        root.Children.Add(_dim);
        root.Children.Add(_canvas);
        _canvas.Children.Add(_sel);
        var hint = UI.Text(mode == ShotMode.Ocr ? "Выделите текст на экране · Esc — отмена" : "Выделите область · Esc — отмена", 13);
        hint.Foreground = Brushes.White; hint.Margin = new Thickness(0, 24, 0, 0);
        hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(hint);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            var v = ScreenshotService.VirtualScreen();
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, v.x, v.y, v.w, v.h, Native.SWP_SHOWWINDOW);
        };
        Loaded += (_, _) => { Activate(); Focus(); };
        Closed += (_, _) => _busy = false;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += (_, e) => { _a = e.GetPosition(_canvas); _drag = true; CaptureMouse(); Draw(_a); };
        MouseMove += (_, e) => { if (_drag) Draw(e.GetPosition(_canvas)); };
        MouseLeftButtonUp += (_, e) => { if (!_drag) return; _drag = false; ReleaseMouseCapture(); Finish(e.GetPosition(_canvas)); };
        MouseRightButtonUp += (_, _) => Close();
    }

    void Draw(Point b)
    {
        double x = Math.Min(_a.X, b.X), y = Math.Min(_a.Y, b.Y), w = Math.Abs(_a.X - b.X), h = Math.Abs(_a.Y - b.Y);
        Canvas.SetLeft(_sel, x); Canvas.SetTop(_sel, y); _sel.Width = w; _sel.Height = h;
        // «окно» без затемнения внутри выделения
        double W = _canvas.ActualWidth, H = _canvas.ActualHeight;
        var g = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, W, H)), new RectangleGeometry(new Rect(x, y, w, h)));
        _dim.Clip = g;
    }

    async void Finish(Point b)
    {
        double W = _canvas.ActualWidth, H = _canvas.ActualHeight;
        double fx = _full.PixelWidth / W, fy = _full.PixelHeight / H;
        int x = (int)(Math.Min(_a.X, b.X) * fx), y = (int)(Math.Min(_a.Y, b.Y) * fy);
        int w = (int)(Math.Abs(_a.X - b.X) * fx), h = (int)(Math.Abs(_a.Y - b.Y) * fy);
        Close();
        if (w < 6 || h < 6) return;
        x = Math.Clamp(x, 0, _full.PixelWidth - 1); y = Math.Clamp(y, 0, _full.PixelHeight - 1);
        w = Math.Min(w, _full.PixelWidth - x); h = Math.Min(h, _full.PixelHeight - y);
        var crop = new CroppedBitmap(_full, new Int32Rect(x, y, w, h)); crop.Freeze();

        if (_mode == ShotMode.Region) new EditorWindow(crop).Show();
        else await OcrResultWindow.ShowFor(crop);
    }
}

/// <summary>Результат распознавания: текст, «Копировать», «Перевести».</summary>
public class OcrResultWindow : Window
{
    public static async Task ShowFor(BitmapSource img)
    {
        var w = new OcrResultWindow();
        w.Show();
        w._text.Text = "Распознаю…";
        w._text.Text = await OcrService.RecognizeAsync(img);
        if (string.IsNullOrWhiteSpace(w._text.Text)) w._text.Text = "Текст не найден. Попробуйте выделить область крупнее.";
    }

    readonly TextBox _text = UI.Input();

    OcrResultWindow()
    {
        Title = "Текст с экрана"; Width = 520; Height = 360; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Bg");
        FontFamily = new FontFamily("Segoe UI");
        _text.AcceptsReturn = true; _text.TextWrapping = TextWrapping.Wrap; _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        bar.Children.Add(UI.Btn("Копировать", () => { try { Clipboard.SetText(_text.Text); } catch { } }));
        bar.Children.Add(UI.Btn("Перевести", async () =>
        {
            var src = _text.Text; _text.Text = "Перевожу…";
            _text.Text = await TranslateService.TranslateAsync(src);
        }, true));
        var dock = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar); dock.Children.Add(_text);
        Content = dock;
    }
}
