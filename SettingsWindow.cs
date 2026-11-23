using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok.Views;

/// <summary>Окно настроек с боковым меню.</summary>
public class SettingsWindow : Window
{
    static SettingsWindow _inst;
    readonly MainWindow _win;
    readonly Grid _host = new() { Margin = new Thickness(26, 20, 26, 16) };
    readonly List<(string name, Button btn)> _items = new();
    readonly Dictionary<string, TabPage> _pages = new();

    // tab == null → раздел внутри SettingsTab с таким же названием
    static readonly (string glyph, string title, string tab)[] Items =
    {
        ("", "Общие", null),
        ("", "Персонаж", null),
        ("", "Праздники", "Праздники"),
        ("", "Клавиши", null),
        ("", "ИИ", null),
        ("", "Погода", null),
        ("", "Звук", null),
        ("", "Живые события", null),
        ("", "Буфер обмена", null),
        ("", "Модули", "Вкладки"),
    };

    public static void ShowIt(MainWindow win, string page = null)
    {
        if (_inst == null) { _inst = new SettingsWindow(win); _inst.Closed += (_, _) => _inst = null; _inst.Show(); }
        else _inst.Activate();
        if (page != null) _inst.Go(page);
    }

    SettingsWindow(MainWindow win)
    {
        _win = win;
        Title = "Островок — настройки"; Width = 900; Height = 620; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true;
        // ФОН ЗАВИСИТ ОТ ТЕМЫ: раньше он задавался один раз при открытии, и после переключения «Тёмная тема»
        // белый/чёрный текст сливался с фоном — в окне оставались только галочки.
        SetResourceReference(BackgroundProperty, "WinBg");
        Theme.Applied += OnTheme;
        Closed += (_, _) => Theme.Applied -= OnTheme;
        SourceInitialized += (_, _) => ApplyChrome();

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var side = new StackPanel { Margin = new Thickness(12, 18, 12, 12) };
        var sideScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = side };
        var sideBox = new Border { Child = sideScroll };
        sideBox.SetResourceReference(Border.BackgroundProperty, "SideBg");

        var head = UI.Text("Островок", 18, true); head.Margin = new Thickness(10, 0, 0, 2);
        var sub = UI.Text("Настройки", 11.5, false, true); sub.Margin = new Thickness(10, 0, 0, 16);
        side.Children.Add(head); side.Children.Add(sub);
        foreach (var it in Items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var gl = new TextBlock { Text = it.glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Width = 28, VerticalAlignment = VerticalAlignment.Center };
            gl.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            sp.Children.Add(gl);
            sp.Children.Add(UI.Text(it.title, 13));
            var key = it.title;
            var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("IconBtn"), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 3),
                                 HorizontalContentAlignment = HorizontalAlignment.Left };
            b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent;
            b.Click += (_, _) => Go(key);
            side.Children.Add(b); _items.Add((key, b));
        }
        var ver = UI.Text("Изменения сохраняются сразу", 10.5, false, true); ver.Margin = new Thickness(10, 18, 0, 0); ver.TextWrapping = TextWrapping.Wrap; ver.TextTrimming = TextTrimming.None;
        side.Children.Add(ver);
        Grid.SetColumn(sideBox, 0); root.Children.Add(sideBox);
        Grid.SetColumn(_host, 1); root.Children.Add(_host);
        Content = root;
        Go("Общие");
    }

    void OnTheme() => Dispatcher.BeginInvoke(new Action(ApplyChrome));

    /// <summary>Заголовок окна в цвет темы (тёмный/светлый).</summary>
    void ApplyChrome()
    {
        try
        {
            bool dark = Hub.S.DarkTheme;
            var h = new WindowInteropHelper(this).EnsureHandle();
            Native.StyleTitleBar(h, dark, dark ? 0x000F0C0C : 0x00F8F4F3);   // COLORREF = 0x00BBGGRR
        }
        catch { }
    }

    void Go(string key)
    {
        _host.Children.Clear();
        if (!_pages.TryGetValue(key, out var p))
        {
            var it = Items.First(x => x.title == key);
            p = it.tab != null ? _win.MakePage(it.tab) : new SettingsTab(_win, key);
            _pages[key] = p;
        }
        _host.Children.Add(p);
        p.OnShown();
        foreach (var (n, b) in _items)
        {
            if (n == key) b.SetResourceReference(Control.BackgroundProperty, "ChipBg");
            else b.Background = Brushes.Transparent;
        }
    }
}
