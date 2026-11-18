using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok.Views;

// ═════════════ 📅 Календарь ═════════════
public class CalendarTab : TabPage
{
    static readonly CultureInfo Ru = new("ru-RU");
    readonly TextBox _in = UI.Input("18:30 созвон · завтра 9:00 врач · через 20 мин чай");
    readonly TextBlock _status = UI.Text("", 11, false, true);
    readonly Border _gridHost = new();
    readonly Border _side = new();
    DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    DateTime _sel = DateTime.Today;
    bool _setup;
    string _setupMsg = "";

    public CalendarTab()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _in.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (Hub.Reminders.Add(_in.Text)) { _in.Clear(); _status.Text = "Напоминание добавлено ✓"; }
            else _status.Text = "Не понял время. Примеры: 18:30 · завтра 9:00 · через 20 мин";
        };
        SetRow(_in, 0); Children.Add(_in);
        _status.Margin = new Thickness(2, 3, 0, 4); SetRow(_status, 1); Children.Add(_status);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(246) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _side.Margin = new Thickness(10, 0, 0, 0);
        SetColumn(_side, 1);
        body.Children.Add(_gridHost); body.Children.Add(_side);
        SetRow(body, 2); Children.Add(body);

        Hub.Reminders.Changed += () => Dispatcher.BeginInvoke(Rebuild);
    }

    public override void OnShown()
    {
        _status.Text = "";
        _setup = string.IsNullOrWhiteSpace(Hub.S.IcsUrl);
        Rebuild();
    }

    // события по дням: ICS + напоминания
    record Entry(DateTime When, string Text, bool AllDay, ReminderItem Rem);

    Dictionary<DateTime, List<Entry>> Collect()
    {
        var d = new Dictionary<DateTime, List<Entry>>();
        void Put(DateTime day, Entry e) { if (!d.TryGetValue(day.Date, out var l)) d[day.Date] = l = new(); l.Add(e); }
        foreach (var e in Hub.Reminders.IcsEvents)
        {
            var last = e.End.HasValue && e.AllDay ? e.End.Value.AddDays(-1).Date : (e.End.HasValue && e.End.Value.Date > e.Start.Date && e.End.Value.TimeOfDay > TimeSpan.Zero ? e.End.Value.Date : e.Start.Date);
            if (last < e.Start.Date) last = e.Start.Date;
            int n = 0;
            for (var day = e.Start.Date; day <= last && n < 31; day = day.AddDays(1), n++) Put(day, new Entry(e.Start, e.Title, e.AllDay, null));
        }
        foreach (var r in Hub.S.Reminders.Where(r => !r.Fired)) Put(r.When, new Entry(r.When, r.Text, false, r));
        return d;
    }

    void Rebuild()
    {
        var ev = Collect();
        BuildMonth(ev);
        BuildSide(ev);
    }

    // ───────── месяц ─────────
    void BuildMonth(Dictionary<DateTime, List<Entry>> ev)
    {
        var p = new StackPanel();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var prev = UI.Btn("‹", () => { _month = _month.AddMonths(-1); Rebuild(); });
        var next = UI.Btn("›", () => { _month = _month.AddMonths(1); Rebuild(); });
        DockPanel.SetDock(prev, Dock.Left); DockPanel.SetDock(next, Dock.Right);
        head.Children.Add(prev); head.Children.Add(next);
        var title = Ru.TextInfo.ToTitleCase(_month.ToString("MMMM yyyy", Ru));
        var tb = UI.Text(title, 13.5, true); tb.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(Links.Clickable(tb, () => { _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); _sel = DateTime.Today; Rebuild(); }));
        p.Children.Add(head);

        var wk = new UniformGrid { Columns = 7 };
        foreach (var n in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" })
        {
            var t = UI.Text(n, 10.5, false, true); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 2);
            wk.Children.Add(t);
        }
        p.Children.Add(wk);

        var grid = new UniformGrid { Columns = 7 };
        var first = _month;
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        for (int i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            grid.Children.Add(Cell(day, day.Month == _month.Month, ev.ContainsKey(day.Date)));
        }
        p.Children.Add(grid);

        var legend = UI.Text("○ в кружке — дни, где есть события", 10, false, true);
        legend.Margin = new Thickness(0, 4, 0, 0); legend.HorizontalAlignment = HorizontalAlignment.Center;
        p.Children.Add(legend);
        _gridHost.Child = p;
    }

    UIElement Cell(DateTime day, bool inMonth, bool hasEvents)
    {
        bool today = day.Date == DateTime.Today, sel = day.Date == _sel.Date;
        var ring = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                BorderThickness = new Thickness(hasEvents ? 2 : 0) };
        if (hasEvents) ring.SetResourceReference(Border.BorderBrushProperty, "Accent");
        if (today) ring.SetResourceReference(Border.BackgroundProperty, "Accent");
        else if (sel) ring.SetResourceReference(Border.BackgroundProperty, "CardHover");
        var t = UI.Text(day.Day.ToString(), 12, today || sel, !inMonth && !today);
        t.HorizontalAlignment = HorizontalAlignment.Center; t.TextTrimming = TextTrimming.None;
        if (today) t.Foreground = Brushes.White;
        ring.Child = t;
        // выбранный день: дополнительное внешнее кольцо
        var outer = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(sel ? 1.2 : 0),
                                 HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = ring };
        if (sel) outer.SetResourceReference(Border.BorderBrushProperty, "Fg");
        var d = day;
        return Links.Clickable(outer, () => { _sel = d.Date; if (d.Month != _month.Month) _month = new DateTime(d.Year, d.Month, 1); Rebuild(); });
    }

    // ───────── правая колонка ─────────
    void BuildSide(Dictionary<DateTime, List<Entry>> ev)
    {
        var dock = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var gear = UI.Btn(_setup ? "✕" : "🔗", () => { _setup = !_setup; _setupMsg = ""; Rebuild(); });
        gear.ToolTip = "Ссылка на Google Календарь";
        DockPanel.SetDock(gear, Dock.Right);
        top.Children.Add(gear);
        string dayName = _sel.Date == DateTime.Today ? "Сегодня" : _sel.Date == DateTime.Today.AddDays(1) ? "Завтра" : Ru.TextInfo.ToTitleCase(_sel.ToString("dddd", Ru));
        var tt = new StackPanel();
        tt.Children.Add(UI.Text(dayName, 14, true));
        tt.Children.Add(UI.Text(_sel.ToString("d MMMM yyyy", Ru), 11, false, true));
        top.Children.Add(tt);
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        if (_setup) dock.Children.Add(SetupPanel());
        else
        {
            var list = new StackPanel();
            if (!ev.TryGetValue(_sel.Date, out var items) || items.Count == 0)
                list.Children.Add(UI.Text("В этот день событий нет", 12, false, true));
            else
                foreach (var e in items.OrderBy(i => i.AllDay ? 0 : 1).ThenBy(i => i.When))
                {
                    var d = new DockPanel();
                    if (e.Rem != null)
                    {
                        var rem = e.Rem;
                        var del = UI.Btn("✕", () => Hub.Reminders.Remove(rem));
                        DockPanel.SetDock(del, Dock.Right); d.Children.Add(del);
                    }
                    var c = new StackPanel();
                    c.Children.Add(UI.Text((e.Rem != null ? "🔔 " : "📅 ") + e.Text, 12.5, true));
                    c.Children.Add(UI.Text(e.AllDay ? "весь день" : e.When.ToString("HH:mm"), 11, false, true));
                    d.Children.Add(c);
                    list.Children.Add(UI.Card(d, 8));
                }
            if (!string.IsNullOrWhiteSpace(Hub.S.IcsUrl))
            {
                var info = UI.Text($"✓ Google Календарь подключён · {Hub.Reminders.IcsCount} событий", 10.5, false, true);
                info.Margin = new Thickness(0, 4, 0, 0);
                list.Children.Add(info);
            }
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        }
        _side.Child = dock;
    }

    UIElement SetupPanel()
    {
        var p = new StackPanel();
        var h = UI.Text("Вставьте ссылку на свой календарь — и дни с событиями будут обведены кружком.", 11.5, false, true);
        h.TextWrapping = TextWrapping.Wrap; h.TextTrimming = TextTrimming.None; p.Children.Add(h);
        var how = UI.Text("Google Календарь → Настройки → ваш календарь → «Интеграция календаря» → «Секретный адрес в формате iCal».", 10.5, false, true);
        how.TextWrapping = TextWrapping.Wrap; how.TextTrimming = TextTrimming.None; how.Margin = new Thickness(0, 4, 0, 6); p.Children.Add(how);

        var box = UI.Input("https://calendar.google.com/calendar/ical/…/basic.ics");
        box.Text = Hub.S.IcsUrl ?? "";
        p.Children.Add(box);

        var msg = UI.Text(_setupMsg, 11); msg.TextWrapping = TextWrapping.Wrap; msg.TextTrimming = TextTrimming.None; msg.Margin = new Thickness(0, 4, 0, 0);
        msg.Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 120));

        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(UI.Btn("Подключить", async () =>
        {
            var u = box.Text.Trim();
            if (u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) u = "https://" + u[9..];
            if (u.Length > 0 && !u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { msg.Text = "Нужна ссылка, которая начинается с https://"; return; }
            if (u.Contains("calendar.google.com/calendar", StringComparison.OrdinalIgnoreCase) && !u.Contains(".ics", StringComparison.OrdinalIgnoreCase))
            { msg.Text = "Это не та ссылка. Нужен «Секретный адрес в формате iCal» — он заканчивается на basic.ics"; return; }
            Hub.S.IcsUrl = u; Hub.S.Save();
            msg.Foreground = (Brush)Application.Current.Resources["FgDim"]; msg.Text = "Загружаю…";
            var err = await Hub.Reminders.LoadIcsAsync();
            if (err != null) { msg.Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 120)); msg.Text = err; return; }
            _setup = u.Length == 0; _setupMsg = "";
            _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            Rebuild();
        }, true));
        row.Children.Add(UI.Btn("Где взять ссылку", () => Links.Open("https://calendar.google.com/calendar/u/0/r/settings")));
        p.Children.Add(row);
        p.Children.Add(msg);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = p };
    }
}
