using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ostrovok.Services;

namespace Ostrovok.Views;

/// <summary>Редактор скриншота: ручка, маркер, ластик, распознать, перевести, сохранить, копировать.</summary>
public class EditorWindow : Window
{
    readonly BitmapSource _img;
    readonly InkCanvas _ink = new() { Background = Brushes.Transparent, EditingMode = InkCanvasEditingMode.Ink };
    readonly Grid _stage = new();
    readonly TextBox _ocrBox = UI.Input();
    Color _color = Colors.Red;

    public EditorWindow(BitmapSource img)
    {
        _img = img;
        Title = "Скриншот"; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Bg");
        FontFamily = new FontFamily("Segoe UI");
        double maxW = SystemParameters.WorkArea.Width * 0.8, maxH = SystemParameters.WorkArea.Height * 0.7;
        double s = Math.Min(1, Math.Min(maxW / img.PixelWidth, maxH / img.PixelHeight));
        // размеры в DIP: изображение рисуется в натуральную величину, окно подстраивается
        double dipW = img.PixelWidth * s, dipH = img.PixelHeight * s;
        Width = Math.Max(560, dipW + 40); Height = dipH + 170;

        _stage.Width = dipW; _stage.Height = dipH;
        _stage.Children.Add(new Image { Source = img, Stretch = Stretch.Fill });
        _stage.Children.Add(_ink);
        SetPen(false);

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(UI.Btn("✏️ Ручка", () => { _ink.EditingMode = InkCanvasEditingMode.Ink; SetPen(false); }));
        bar.Children.Add(UI.Btn("🖍 Маркер", () => { _ink.EditingMode = InkCanvasEditingMode.Ink; SetPen(true); }));
        bar.Children.Add(UI.Btn("🧽 Ластик", () => _ink.EditingMode = InkCanvasEditingMode.EraseByStroke));
        foreach (var c in new[] { Colors.Red, Colors.Yellow, Colors.LimeGreen, Colors.DeepSkyBlue, Colors.White })
        {
            var col = c;
            var sw = new Button { Width = 22, Height = 22, Margin = new Thickness(3), Background = new SolidColorBrush(col), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            sw.Click += (_, _) => { _color = col; _ink.DefaultDrawingAttributes.Color = col; };
            bar.Children.Add(sw);
        }
        bar.Children.Add(UI.Btn("↩ Отменить", () => { if (_ink.Strokes.Count > 0) _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1); }));
        bar.Children.Add(UI.Btn("🔤 Распознать", async () => await Recognize(false), true));
        bar.Children.Add(UI.Btn("🌐 Перевести", async () => await Recognize(true), true));
        bar.Children.Add(UI.Btn("📋 Копировать", CopyImage));
        bar.Children.Add(UI.Btn("💾 Сохранить", Save));

        _ocrBox.AcceptsReturn = true; _ocrBox.TextWrapping = TextWrapping.Wrap; _ocrBox.Height = 70;
        _ocrBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; _ocrBox.Visibility = Visibility.Collapsed;
        _ocrBox.Margin = new Thickness(0, 8, 0, 0);

        var dock = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(bar, Dock.Top); DockPanel.SetDock(_ocrBox, Dock.Bottom);
        dock.Children.Add(bar); dock.Children.Add(_ocrBox);
        dock.Children.Add(new Viewbox { Child = _stage, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        Content = dock;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.Z && _ink.Strokes.Count > 0) _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1);
                else if (e.Key == Key.C && !_ocrBox.IsKeyboardFocused) CopyImage();
                else if (e.Key == Key.S) Save();
            }
        };
    }

    void SetPen(bool marker)
    {
        var a = _ink.DefaultDrawingAttributes;
        a.Color = _color; a.IsHighlighter = marker; a.FitToCurve = true;
        a.Width = a.Height = marker ? 14 : 3.5;
    }

    BitmapSource Render()
    {
        // сцена (снимок + рисунок) в натуральном разрешении исходного снимка
        double k = _img.PixelWidth / _stage.Width;
        _stage.UpdateLayout();
        var rtb = new RenderTargetBitmap(_img.PixelWidth, _img.PixelHeight, 96 * k, 96 * k, PixelFormats.Pbgra32);
        rtb.Render(_stage);
        rtb.Freeze();
        return rtb;
    }

    void CopyImage() { try { Clipboard.SetImage(Render()); } catch (Exception ex) { AppSettings.Log("copy img: " + ex.Message); } }

    void Save()
    {
        try
        {
            var path = ScreenshotService.SaveToPictures(Render());
            Hub.Notices.Push("Скриншот", "💾 Сохранено", Path.GetFileName(path));
        }
        catch (Exception ex) { AppSettings.Log("save img: " + ex.Message); }
    }

    async Task Recognize(bool translate)
    {
        _ocrBox.Visibility = Visibility.Visible;
        _ocrBox.Text = "Распознаю…";
        var text = await OcrService.RecognizeAsync(_img);
        if (string.IsNullOrWhiteSpace(text)) { _ocrBox.Text = "Текст не найден."; return; }
        if (translate) { _ocrBox.Text = "Перевожу…"; text = await TranslateService.TranslateAsync(text); }
        _ocrBox.Text = text;
    }
}
