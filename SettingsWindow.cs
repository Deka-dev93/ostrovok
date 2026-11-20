using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok.Views;

/// <summary>Окно настроек с боковым меню (как в Island): Общие, Вкладки, Праздники.</summary>
public class SettingsWindow : Window
{
    static SettingsWindow _inst;
    readonly MainWindow _win;
    readonly Grid _host = new() { Margin = new Thickness(18, 14, 18, 14) };
    readonly List<(string name, Button btn)> _items = new();
    readonly Dictionary<string, TabPage> _pages = new();

    static readonly (string glyph, string title, string tab)[] Items =
    {
        ("", "Общие", "Настройки"),
        ("", "Вкладки", "Вкладки"),
        ("", "Праздники", "Праздники"),
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
        Title = "Островок — настройки"; Width = 860; Height = 560; MinWidth = 700; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hub.S.DarkTheme ? "#FF0C0C0F" : "#FFF3F4F8"));
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var side = new StackPanel { Margin = new Thickness(12, 16, 8, 12) };
        var head = UI.Text("Островок", 16, true); head.Margin = new Thickness(8, 0, 0, 14);
        side.Children.Add(head);
        foreach (var it in Items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = it.glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Width = 24, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Fg"] });
            sp.Children.Add(UI.Text(it.title, 13));
            var tab = it.tab;
            var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("IconBtn"), HorizontalContentAlignment = HorizontalAlignment.Left,
                                 Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 3) };
            b.Background = Brushes.Transparent;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.Click += (_, _) => Go(tab);
            side.Children.Add(b); _items.Add((tab, b));
        }
        var ver = UI.Text("Изменения сохраняются сразу", 10.5, false, true); ver.Margin = new Thickness(8, 24, 0, 0); ver.TextWrapping = TextWrapping.Wrap; ver.TextTrimming = TextTrimming.None;
        side.Children.Add(ver);
        Grid.SetColumn(side, 0); root.Children.Add(side);
        Grid.SetColumn(_host, 1); root.Children.Add(_host);
        Content = root;
        Go("Настройки");
    }

    void Go(string tab)
    {
        _host.Children.Clear();
        if (!_pages.TryGetValue(tab, out var p)) { p = _win.MakePage(tab); _pages[tab] = p; }
        _host.Children.Add(p);
        p.OnShown();
        foreach (var (n, b) in _items)
        {
            if (n == tab) b.SetResourceReference(Control.BackgroundProperty, "Card");
            else b.Background = Brushes.Transparent;
        }
    }
}
