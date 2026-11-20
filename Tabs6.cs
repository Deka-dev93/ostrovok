using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ostrovok.Services;

namespace Ostrovok.Views;

// ═════════════ 🧩 Вкладки ═════════════
public class CustomizeTab : TabPage
{
    readonly MainWindow _win;
    readonly StackPanel _tabsList = new();

    public CustomizeTab(MainWindow win)
    {
        _win = win;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // левая колонка: вкладки
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var h = UI.Text("Вкладки: галочка — показывать, стрелки — порядок", 11.5, true, true);
        h.Margin = new Thickness(0, 0, 0, 4); DockPanel.SetDock(h, Dock.Top); left.Children.Add(h);
        left.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tabsList });
        SetColumn(left, 0); Children.Add(left);

        // правая колонка: остров
        var r = new StackPanel();
        r.Children.Add(UI.Text("Остров", 11.5, true, true));
        r.Children.Add(Check("Погода в свёрнутой пилюле", () => Hub.S.ShowWeatherInPill, v => { Hub.S.ShowWeatherInPill = v; }));
        r.Children.Add(Check("Персонаж", () => Hub.S.ShowCharacter, v => { Hub.S.ShowCharacter = v; _win.ApplyCharacter(); }));
        var n = UI.Text("Показывать уведомления на острове", 11.5, true, true); n.Margin = new Thickness(0, 8, 0, 0); r.Children.Add(n);
        r.Children.Add(Check("📧 Почта", () => Hub.S.PopMail, v => Hub.S.PopMail = v));
        r.Children.Add(Check("✈️ Telegram", () => Hub.S.PopTelegram, v => Hub.S.PopTelegram = v));
        r.Children.Add(Check("🎧 Наушники", () => Hub.S.PopHeadphones, v => Hub.S.PopHeadphones = v));
        r.Children.Add(Check("⏱ Таймеры, напоминания, остальное", () => Hub.S.PopSystem, v => Hub.S.PopSystem = v));
        var reset = UI.Btn("↺ Вернуть вкладки как были", () =>
        {
            Hub.S.HiddenTabs.Clear(); Hub.S.TabOrder.Clear(); Hub.S.Save(); _win.ApplyTabs(); BuildTabsList();
        });
        reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Margin = new Thickness(0, 10, 0, 0);
        r.Children.Add(reset);
        var rs = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = r };
        SetColumn(rs, 1); Children.Add(rs);

        BuildTabsList();
    }

    public override void OnShown() => BuildTabsList();

    void BuildTabsList()
    {
        _tabsList.Children.Clear();
        var infos = _win.TabInfos();
        var movable = infos.Where(i => !MainWindow.IsPinnedTab(i.name)).Select(i => i.name).ToList();

        foreach (var (icon, name) in infos)
        {
            string nm = name;
            bool pinned = MainWindow.IsPinnedTab(nm);
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };

            if (!pinned)
            {
                int idx = movable.IndexOf(nm);
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(btns, Dock.Right);
                var up = UI.Btn("▲", () => Move(movable, idx, -1)); up.IsEnabled = idx > 0; up.Padding = new Thickness(7, 2, 7, 2);
                var dn = UI.Btn("▼", () => Move(movable, idx, 1)); dn.IsEnabled = idx < movable.Count - 1; dn.Padding = new Thickness(7, 2, 7, 2);
                btns.Children.Add(up); btns.Children.Add(dn);
                row.Children.Add(btns);
            }

            var cb = new CheckBox { IsChecked = pinned || !Hub.S.HiddenTabs.Contains(nm), IsEnabled = !pinned, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            cb.Content = UI.Text($"{icon}  {nm}" + (pinned ? "  (всегда)" : ""), 12.5);
            cb.Checked += (_, _) => SetHidden(nm, false);
            cb.Unchecked += (_, _) => SetHidden(nm, true);
            row.Children.Add(cb);
            _tabsList.Children.Add(row);
        }
    }

    void SetHidden(string name, bool hidden)
    {
        var h = Hub.S.HiddenTabs;
        if (hidden && !h.Contains(name)) h.Add(name);
        if (!hidden) h.Remove(name);
        Hub.S.Save();
        _win.ApplyTabs();
    }

    void Move(List<string> order, int idx, int delta)
    {
        int to = idx + delta;
        if (to < 0 || to >= order.Count) return;
        (order[idx], order[to]) = (order[to], order[idx]);
        Hub.S.TabOrder = order.ToList();
        Hub.S.Save();
        _win.ApplyTabs();
        BuildTabsList();
    }

    static CheckBox Check(string text, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { IsChecked = get(), Style = (Style)Application.Current.FindResource("Switch") };
        c.Content = UI.Text(text, 12.5);
        c.Checked += (_, _) => { set(true); Hub.S.Save(); };
        c.Unchecked += (_, _) => { set(false); Hub.S.Save(); };
        return c;
    }
}
