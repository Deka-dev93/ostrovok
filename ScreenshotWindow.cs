using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Ostrovok.Services;

namespace Ostrovok.Views;

public enum ShotMode { Region, Ocr, Qr, Pipette }

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

    // пипетка
    readonly Border _lens = new() { Width = 124, Height = 150, CornerRadius = new CornerRadius(12), Background = Brushes.Black, BorderBrush = Brushes.White,
                                    BorderThickness = new Thickness(2), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly Image _lensImg = new() { Width = 114, Height = 114, Stretch = Stretch.Fill };
    readonly TextBlock _lensHex = new() { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
    readonly Border _swatch = new() { Height = 14, CornerRadius = new CornerRadius(4), Margin = new Thickness(4, 4, 4, 0), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
    FormatConvertedBitmap _bgra;
    Color _picked;

    public static void StartRegion() => Start(ShotMode.Region);
    public static void StartOcr() => Start(ShotMode.Ocr);
    public static void StartQr() => Start(ShotMode.Qr);
    public static void StartPipette() => Start(ShotMode.Pipette);

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
        var hint = UI.Text(mode == ShotMode.Ocr ? "Выделите текст на экране · Esc — отмена"
                         : mode == ShotMode.Qr ? "Выделите QR-код · Esc — отмена"
                         : mode == ShotMode.Pipette ? "Наведите на цвет и кликните · Esc — отмена" : "Выделите область · Esc — отмена", 13);
        hint.Foreground = Brushes.White; hint.Margin = new Thickness(0, 24, 0, 0);
        hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(hint);
        Content = root;
        if (mode == ShotMode.Pipette)
        {
            _dim.Visibility = Visibility.Collapsed;
            RenderOptions.SetBitmapScalingMode(_lensImg, BitmapScalingMode.NearestNeighbor);
            var lp = new StackPanel { Margin = new Thickness(3) };
            var g = new Grid(); g.Children.Add(_lensImg);
            g.Children.Add(new Rectangle { Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            lp.Children.Add(g); lp.Children.Add(_swatch); lp.Children.Add(_lensHex);
            _lens.Child = lp;
            _canvas.Children.Add(_lens);
        }

        SourceInitialized += (_, _) =>
        {
            var v = ScreenshotService.VirtualScreen();
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, v.x, v.y, v.w, v.h, Native.SWP_SHOWWINDOW);
        };
        Loaded += (_, _) => { Activate(); Focus(); };
        Closed += (_, _) => _busy = false;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (_mode == ShotMode.Pipette) { UpdateLens(e.GetPosition(_canvas)); PickColor(); return; }
            _a = e.GetPosition(_canvas); _drag = true; CaptureMouse(); Draw(_a);
        };
        MouseMove += (_, e) =>
        {
            if (_mode == ShotMode.Pipette) { UpdateLens(e.GetPosition(_canvas)); return; }
            if (_drag) Draw(e.GetPosition(_canvas));
        };
        MouseLeftButtonUp += (_, e) => { if (!_drag) return; _drag = false; ReleaseMouseCapture(); Finish(e.GetPosition(_canvas)); };
        MouseRightButtonUp += (_, _) => Close();
    }

    void UpdateLens(Point p)
    {
        double W = _canvas.ActualWidth, H = _canvas.ActualHeight;
        if (W < 10 || H < 10) return;
        if (_bgra == null) { _bgra = new FormatConvertedBitmap(_full, PixelFormats.Bgra32, null, 0); _bgra.Freeze(); }
        int pw = _bgra.PixelWidth, ph = _bgra.PixelHeight;
        int x = Math.Clamp((int)(p.X * pw / W), 0, pw - 1), y = Math.Clamp((int)(p.Y * ph / H), 0, ph - 1);
        var px = new byte[4];
        _bgra.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        _picked = Color.FromRgb(px[2], px[1], px[0]);
        const int half = 6;
        int cx = Math.Clamp(x, half, pw - half - 1), cy = Math.Clamp(y, half, ph - half - 1);
        var crop = new CroppedBitmap(_bgra, new Int32Rect(cx - half, cy - half, 13, 13)); crop.Freeze();
        _lensImg.Source = crop;
        _lensHex.Text = $"#{_picked.R:X2}{_picked.G:X2}{_picked.B:X2}";
        _swatch.Background = new SolidColorBrush(_picked);
        double lx = p.X + 22, ly = p.Y + 22;
        if (lx + 130 > W) lx = p.X - 146;
        if (ly + 156 > H) ly = p.Y - 172;
        Canvas.SetLeft(_lens, lx); Canvas.SetTop(_lens, ly);
        _lens.Visibility = Visibility.Visible;
    }

    void PickColor()
    {
        string hex = $"#{_picked.R:X2}{_picked.G:X2}{_picked.B:X2}";
        Close();
        try { Clipboard.SetText(hex); } catch { }
        Hub.Notices.Push("Пипетка", "🎨 " + hex, "Цвет скопирован в буфер обмена");
    }

    static string DecodeQr(BitmapSource img)
    {
        var bgra = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var buf = new byte[w * h * 4];
        bgra.CopyPixels(buf, w * 4, 0);
        var reader = new ZXing.BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true, TryInverted = true,
                PossibleFormats = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE }
            }
        };
        var res = reader.Decode(new ZXing.RGBLuminanceSource(buf, w, h, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32));
        return res?.Text;
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
        else if (_mode == ShotMode.Qr)
        {
            string text = null;
            try { text = await Task.Run(() => DecodeQr(crop)); } catch (Exception ex) { AppSettings.Log("qr: " + ex.Message); }
            OcrResultWindow.ShowText("QR-код", string.IsNullOrEmpty(text) ? "QR-код не найден. Выделите его целиком, вместе с белой рамкой." : text);
            if (!string.IsNullOrEmpty(text)) { try { Clipboard.SetText(text); } catch { } }
        }
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

    public static void ShowText(string title, string text)
    {
        var w = new OcrResultWindow(title);
        w._text.Text = text;
        w.Show();
        if (text.StartsWith("http://") || text.StartsWith("https://"))
            w._bar.Children.Insert(1, UI.Btn("Открыть ссылку", () => Links.Open(text.Trim())));
    }

    readonly TextBox _text = UI.Input();
    readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

    OcrResultWindow(string title = "Текст с экрана")
    {
        Title = title; Width = 520; Height = 360; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Bg");
        FontFamily = new FontFamily("Segoe UI");
        _text.AcceptsReturn = true; _text.TextWrapping = TextWrapping.Wrap; _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var bar = _bar;
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
