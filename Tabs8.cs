using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ostrovok.Services;
using QRCoder;

namespace Ostrovok.Views;

// ═════════════ 🗄 Полка ═════════════
public class ShelfTab : TabPage
{
    readonly Image _qr = new() { Width = 128, Height = 128, Margin = new Thickness(0, 6, 0, 4) };
    readonly TextBlock _url = UI.Text("", 10, false, true), _state = UI.Text("", 11.5);
    readonly Button _toggle;
    readonly WrapPanel _tiles = new();
    readonly TextBlock _empty = UI.Text("Перетащите сюда файлы — они лягут на полку.\nОттуда их можно перетащить в любое окно.", 12, false, true);
    string _key = "";
    bool _dirty = true, _dragged;
    Point _down;

    public ShelfTab()
    {
        AllowDrop = true; Background = Brushes.Transparent;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] fs) foreach (var f in fs) if (File.Exists(f)) Hub.Shelf.Add(f);
        };
        Hub.Shelf.Changed += () => _dirty = true;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // телефон
        var ph = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        ph.Children.Add(UI.Text("📱 Телефон", 13, true));
        ph.Children.Add(UI.Text("Наведите камеру — отправляйте файлы и текст на ПК (одна Wi-Fi сеть)", 10.5, false, true));
        ((TextBlock)ph.Children[1]).TextWrapping = TextWrapping.Wrap; ((TextBlock)ph.Children[1]).TextTrimming = TextTrimming.None;
        _qr.HorizontalAlignment = HorizontalAlignment.Left;
        ph.Children.Add(_qr);
        _url.TextWrapping = TextWrapping.Wrap; _url.TextTrimming = TextTrimming.None; ph.Children.Add(_url);
        _toggle = UI.Btn("Включить", async () =>
        {
            Hub.S.ShelfOn = !Hub.S.ShelfOn; Hub.S.Save();
            if (Hub.S.ShelfOn) await Hub.Shelf.StartAsync(); else await Hub.Shelf.StopAsync();
            _dirty = true; Tick();
        });
        _toggle.Margin = new Thickness(0, 6, 0, 0);
        ph.Children.Add(_state); ph.Children.Add(_toggle);
        var dl = new CheckBox { Content = "Показывать загрузки в острове", IsChecked = Hub.S.ShowDownloads, Margin = new Thickness(0, 8, 0, 0), FontSize = 11 };
        dl.SetResourceReference(ForegroundProperty, "FgDim");
        dl.Click += (_, _) => { Hub.S.ShowDownloads = dl.IsChecked == true; Hub.S.Save(); };
        ph.Children.Add(dl);
        SetColumn(ph, 0); Children.Add(ph);

        // полка
        var right = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(UI.Text("🗄 Полка", 13, true));
        bar.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 10, 0));
        bar.Children.Add(UI.Btn("＋ Файлы", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (dlg.ShowDialog() == true) foreach (var f in dlg.FileNames) Hub.Shelf.Add(f);
        }));
        var all = UI.Btn("⇪ Все сразу", () => { });
        all.ToolTip = "Зажмите и перетащите — унесёте все файлы разом";
        all.PreviewMouseLeftButtonDown += (_, e) => _down = e.GetPosition(this);
        all.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(this) - _down).Length < 6) return;
            var fs = Hub.Shelf.Files().ToArray();
            if (fs.Length > 0) DragDrop.DoDragDrop(all, new DataObject(DataFormats.FileDrop, fs), DragDropEffects.Copy);
        };
        bar.Children.Add(all);
        bar.Children.Add(UI.Btn("📂", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ShelfService.Dir) { UseShellExecute = true })));
        bar.Children.Add(UI.Btn("Очистить", () => Hub.Shelf.Clear()));
        DockPanel.SetDock(bar, Dock.Top); right.Children.Add(bar);
        _empty.TextWrapping = TextWrapping.Wrap; _empty.TextTrimming = TextTrimming.None; _empty.Margin = new Thickness(0, 12, 0, 0);
        right.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tiles });
        SetColumn(right, 1); Children.Add(right);
    }

    public override void OnShown() { _dirty = true; Tick(); }

    public override void Tick()
    {
        var sh = Hub.Shelf;
        string k = sh.Url + "|" + sh.Running + "|" + sh.Error + "|" + string.Join("|", sh.Files());
        if (!_dirty && k == _key) return;
        _dirty = false; _key = k;

        _state.Text = sh.Running ? "● Включено" : (sh.Error.Length > 0 ? "Ошибка: " + sh.Error : "○ Выключено");
        _toggle.Content = sh.Running ? "Выключить" : "Включить";
        _url.Text = sh.Running ? sh.Url : "Включите — появится QR-код. Windows может спросить про доступ к сети: разрешите для домашней сети.";
        _qr.Source = sh.Running ? MakeQr(sh.Url) : null;
        _qr.Visibility = sh.Running ? Visibility.Visible : Visibility.Collapsed;

        _tiles.Children.Clear();
        var files = sh.Files();
        if (files.Count == 0) { _tiles.Children.Add(_empty); return; }
        foreach (var f in files) _tiles.Children.Add(Tile(f));
    }

    static BitmapImage MakeQr(string url)
    {
        try
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(8);
            var bi = new BitmapImage();
            bi.BeginInit(); bi.StreamSource = new MemoryStream(png); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    UIElement Tile(string path)
    {
        ImageSource src = null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")
        {
            try { var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(path); bi.DecodePixelWidth = 96; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); src = bi; } catch { }
        }
        src ??= AppDock.Icon(path);

        var sp = new StackPanel { Width = 78 };
        sp.Children.Add(new Image { Source = src, Width = 44, Height = 44, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 4, 0, 3) });
        var name = UI.Text(Path.GetFileName(path), 10.5); name.HorizontalAlignment = HorizontalAlignment.Center; name.MaxWidth = 74;
        sp.Children.Add(name);
        var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("IconBtn"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(4),
                             ToolTip = Path.GetFileName(path) + "\nклик — открыть · перетащить — вынести · правая кнопка — убрать" };
        b.PreviewMouseLeftButtonDown += (_, e) => { _down = e.GetPosition(this); _dragged = false; };
        b.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragged || (e.GetPosition(this) - _down).Length < 6) return;
            _dragged = true;
            DragDrop.DoDragDrop(b, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
        };
        b.Click += (_, _) =>
        {
            if (_dragged) { _dragged = false; return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        };
        b.MouseRightButtonUp += (_, e) => { Hub.Shelf.Remove(path); e.Handled = true; };
        return b;
    }
}
