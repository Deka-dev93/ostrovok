using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok.Views;

/// <summary>Окно настроек с боковым меню (как в Island).</summary>
public class SettingsWindow : Window
{
    static SettingsWindow _inst;
    readonly MainWindow _win;
    readonly Grid _host = new() { Margin = new Thickness(18, 14, 18, 14) };
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
        ("", "Модули", "Вкладки"),
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
        Title = "Островок — настройки"; Width = 880; Height = 600; MinWidth = 700; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hub.S.DarkTheme ? "#FF0C0C0F" : "#FFF3F4F8"));
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var side = new StackPanel { Margin = new Thickness(12, 16, 8, 12) };
        var sideScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = side };
        var head = UI.Text("Островок", 16, true); head.Margin = new Thickness(8, 0, 0, 14);
        side.Children.Add(head);
        foreach (var it in Items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = it.glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Width = 26, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Fg"] });
            sp.Children.Add(UI.Text(it.title, 13));
            var key = it.title;
            var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("IconBtn"), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 3),
                                 HorizontalContentAlignment = HorizontalAlignment.Left };
            b.Background = Brushes.Transparent;
            b.Click += (_, _) => Go(key);
            side.Children.Add(b); _items.Add((key, b));
        }
        var ver = UI.Text("Изменения сохраняются сразу", 10.5, false, true); ver.Margin = new Thickness(8, 18, 0, 0); ver.TextWrapping = TextWrapping.Wrap; ver.TextTrimming = TextTrimming.None;
        side.Children.Add(ver);
        Grid.SetColumn(sideScroll, 0); root.Children.Add(sideScroll);
        Grid.SetColumn(_host, 1); root.Children.Add(_host);
        Content = root;
        Go("Общие");
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
            if (n == key) b.SetResourceReference(Control.BackgroundProperty, "Card");
            else b.Background = Brushes.Transparent;
        }
    }
}
