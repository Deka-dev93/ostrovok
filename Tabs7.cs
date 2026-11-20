using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ostrovok.Services;

namespace Ostrovok.Views;

// ═════════════ 🎄 Праздники ═════════════
public class HolidayTab : TabPage
{
    readonly StackPanel _p = new();

    public HolidayTab()
    {
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _p });
        Build();
    }

    public override void OnShown() => Build();

    static void Changed() { Hub.S.Save(); Holiday.Apply(); }

    void Build()
    {
        _p.Children.Clear();
        _p.Children.Add(Head("Новый год на острове"));
        _p.Children.Add(Seg(new[] { ("По дате (1 дек – 14 янв)", "date"), ("Всегда", "always"), ("Нет", "off") },
            () => Hub.S.HolidayMode, v => { Hub.S.HolidayMode = v; Changed(); }));

        _p.Children.Add(Check("❄️ Снег по всему экрану (мышь проходит сквозь него)", () => Hub.S.HolidaySnowScreen, v => { Hub.S.HolidaySnowScreen = v; Changed(); }));
        _p.Children.Add(Check("🏔 Шапка снега на острове", () => Hub.S.HolidaySnowCap, v => { Hub.S.HolidaySnowCap = v; Changed(); }));

        _p.Children.Add(Head("Гирлянда"));
        _p.Children.Add(Seg(new[] { ("Нет", "Off"), ("Мигает", "Blink"), ("Бежит волной", "Wave"), ("Под музыку", "Music") },
            () => Hub.S.HolidayGarland, v => { Hub.S.HolidayGarland = v; Changed(); }));

        _p.Children.Add(Head("Сколько снега"));
        var label = UI.Text($"{Hub.S.SnowAmount}%", 11.5, false, true);
        var bar = new SeekBar { Value = (Hub.S.SnowAmount - 25) / 175.0, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        bar.Previewing += f => label.Text = $"{(int)(25 + f * 175)}%";
        bar.Committed += f => { Hub.S.SnowAmount = (int)(25 + f * 175); label.Text = Hub.S.SnowAmount + "%"; Changed(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(bar); label.Margin = new Thickness(10, 0, 0, 0); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
        _p.Children.Add(row);

        _p.Children.Add(Head("Праздничный персонаж (следит за курсором)"));
        var chars = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var k in new[] { "" }.Concat(Holiday.Chars))
        {
            var kind = k;
            object content;
            if (kind == "") content = UI.Text("Свой", 12, true);
            else content = new CharacterControl { Kind = kind, Width = 46, Height = 46, IsHitTestVisible = false };
            var b = new Button { Content = content, Padding = new Thickness(6), MinWidth = 46, Margin = new Thickness(0, 0, 6, 6), ToolTip = Holiday.NameOf(kind),
                                 Style = (Style)Application.Current.FindResource("IconBtn") };
            if (kind == Hub.S.HolidayChar) b.SetResourceReference(Control.BackgroundProperty, "Accent");
            b.Click += (_, _) => { Hub.S.HolidayChar = kind; Changed(); Build(); };
            chars.Children.Add(b);
        }
        _p.Children.Add(chars);
    }

    static TextBlock Head(string s) { var t = UI.Text(s, 12, true, true); t.Margin = new Thickness(0, 8, 0, 4); return t; }

    static UIElement Check(string text, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { IsChecked = get(), Style = (Style)Application.Current.FindResource("Switch") };
        c.Content = UI.Text(text, 12.5);
        c.Checked += (_, _) => set(true);
        c.Unchecked += (_, _) => set(false);
        return c;
    }

    /// <summary>Ряд кнопок-вариантов, выбранная подсвечена цветом.</summary>
    static UIElement Seg((string label, string value)[] items, Func<string> get, Action<string> set)
    {
        var wrap = new WrapPanel();
        var btns = new List<(Button b, string v)>();
        void Paint()
        {
            foreach (var (b, v) in btns)
                b.SetResourceReference(Control.BackgroundProperty, v == get() ? "Accent" : "Card");
        }
        foreach (var (label, value) in items)
        {
            string v = value;
            var b = UI.Btn(label, () => { set(v); Paint(); });
            btns.Add((b, v)); wrap.Children.Add(b);
        }
        Paint();
        return wrap;
    }
}
